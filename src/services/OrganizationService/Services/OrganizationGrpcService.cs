using OrganizationService.Models;
using OrganizationService.Repositories;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HRSystem.Protos.Employee.V1;
using HRSystem.Protos.Organization.V1;
using HRSystem.Shared.Audit;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace OrganizationService.Services;

public class OrganizationGrpcService : HRSystem.Protos.Organization.V1.OrganizationService.OrganizationServiceBase
{
    private readonly IOrganizationRepository _repository;
    private readonly EmployeeService.EmployeeServiceClient _employeeClient;
    private readonly ILogger<OrganizationGrpcService> _logger;

    public OrganizationGrpcService(
        IOrganizationRepository repository,
        EmployeeService.EmployeeServiceClient employeeClient,
        ILogger<OrganizationGrpcService> logger)
    {
        _repository = repository;
        _employeeClient = employeeClient;
        _logger = logger;
    }

    [Audit(AuditEventType.OrganizationCreated)]
    public override async Task<Organization> CreateOrganization(CreateOrganizationRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Name is required."));

        var document = new OrganizationDocument
        {
            Name = request.Name,
            ParentOrgId = request.ParentOrgId,
            ManagerId = request.ManagerId,
            Description = request.Description
        };

        try
        {
            var created = await _repository.CreateAsync(document, context.CancellationToken);
            return ToProto(created);
        }
        catch (ParentOrganizationNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos error creating organization");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to create organization."));
        }
    }

    [NoAudit("read-only query, not auditable")]
    public override async Task<Organization> GetOrganization(GetOrganizationRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.OrgId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "OrgId is required."));

        try
        {
            var document = await _repository.GetAsync(request.OrgId, context.CancellationToken);
            if (document is null)
                throw new RpcException(new Status(StatusCode.NotFound, $"Organization '{request.OrgId}' not found."));

            return ToProto(document);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting organization {OrgId}", request.OrgId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to get organization."));
        }
    }

    [Audit(AuditEventType.OrganizationChanged)]
    public override async Task<Organization> UpdateOrganization(UpdateOrganizationRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.OrgId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "OrgId is required."));

        try
        {
            var existing = await _repository.GetAsync(request.OrgId, context.CancellationToken)
                ?? throw new RpcException(new Status(StatusCode.NotFound, $"Organization '{request.OrgId}' not found."));

            if (request.HasName) existing.Name = request.Name;
            if (request.HasManagerId) existing.ManagerId = request.ManagerId;
            if (request.HasDescription) existing.Description = request.Description;

            if (request.HasParentOrgId)
            {
                var newParentOrgId = request.ParentOrgId;

                if (!string.IsNullOrEmpty(newParentOrgId) && newParentOrgId != existing.ParentOrgId)
                {
                    // Verify the new parent exists
                    var newParent = await _repository.GetAsync(newParentOrgId, context.CancellationToken)
                        ?? throw new RpcException(new Status(StatusCode.NotFound, $"Parent organization '{newParentOrgId}' not found."));

                    // Circular reference check: walk up from the new parent to ensure we don't encounter the target org
                    await CheckCircularReferenceAsync(request.OrgId, newParentOrgId, context.CancellationToken);

                    existing.ParentOrgId = newParentOrgId;
                    existing.Level = newParent.Level + 1;
                }
                else if (string.IsNullOrEmpty(newParentOrgId))
                {
                    existing.ParentOrgId = string.Empty;
                    existing.Level = 0;
                }
            }

            var updated = await _repository.UpdateAsync(existing, context.CancellationToken);
            return ToProto(updated);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating organization {OrgId}", request.OrgId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to update organization."));
        }
    }

    [Audit(AuditEventType.OrganizationDeleted)]
    public override async Task<Empty> DeleteOrganization(DeleteOrganizationRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.OrgId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "OrgId is required."));

        // UUID format validation (docs/api-spec.md §3.4 — 400 INVALID_ARGUMENT for malformed orgId).
        if (!Guid.TryParse(request.OrgId, out _))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "orgId must be a valid UUID."));

        try
        {
            // 1) Existence check (NOT_FOUND wins over precondition checks).
            var existing = await _repository.GetAsync(request.OrgId, context.CancellationToken)
                ?? throw new RpcException(new Status(StatusCode.NotFound, $"Organization '{request.OrgId}' not found."));

            // 2) Children check — lightweight existence query (SELECT VALUE 1 ... LIMIT 1) to avoid
            //    fetching all children just to compute count > 0 on large tenants.
            var hasChildren = await _repository.HasChildrenAsync(request.OrgId, context.CancellationToken);
            if (hasChildren)
            {
                _logger.LogInformation(
                    "DeleteOrganization rejected: org {OrgId} has child organisations",
                    request.OrgId);
                throw new RpcException(new Status(
                    StatusCode.FailedPrecondition,
                    $"Organization '{request.OrgId}' has child organization(s) and cannot be deleted."));
            }

            // 3) Cross-service employee check (cost: 1 gRPC + 1 RU on EmployeeService).
            bool hasEmployees;
            try
            {
                var resp = await _employeeClient.HasEmployeesByDepartmentAsync(
                    new HasEmployeesByDepartmentRequest { DepartmentId = request.OrgId },
                    cancellationToken: context.CancellationToken);
                hasEmployees = resp.HasEmployees;
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex,
                    "EmployeeService.HasEmployeesByDepartment failed for {OrgId}; aborting delete",
                    request.OrgId);
                // Surface as Unavailable so callers can retry; never silently delete on dependency failure.
                throw new RpcException(new Status(
                    StatusCode.Unavailable,
                    "Employee service unavailable; cannot verify department membership."));
            }

            if (hasEmployees)
            {
                _logger.LogInformation(
                    "DeleteOrganization rejected: org {OrgId} still has assigned employees",
                    request.OrgId);
                throw new RpcException(new Status(
                    StatusCode.FailedPrecondition,
                    $"Organization '{request.OrgId}' still has assigned employees and cannot be deleted."));
            }

            // 4) Physical delete. A `false` return means the document vanished between checks
            // (race) — surface as NOT_FOUND so callers can detect.
            var deleted = await _repository.DeleteAsync(request.OrgId, context.CancellationToken);
            if (!deleted)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Organization '{request.OrgId}' not found."));
            }

            _logger.LogInformation(
                "Deleted organization {OrgId} (name snapshot: {OrgName})",
                existing.OrgId, existing.Name);

            return new Empty();
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting organization {OrgId}", request.OrgId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to delete organization."));
        }
    }

    [NoAudit("read-only query, not auditable")]
    public override async Task<ListChildrenResponse> ListChildren(ListChildrenRequest request, ServerCallContext context)
    {
        // When orgId is empty, list root organizations (parentOrgId == "")
        var orgId = string.IsNullOrWhiteSpace(request.OrgId) ? "" : request.OrgId;
        var limit = request.Limit > 0 ? request.Limit : 20;
        var cursor = string.IsNullOrEmpty(request.Cursor) ? null : request.Cursor;

        try
        {
            var (items, continuationToken, hasMore) = await _repository.ListChildrenAsync(
                orgId, limit, cursor, context.CancellationToken);

            var response = new ListChildrenResponse
            {
                NextCursor = continuationToken ?? string.Empty,
                HasMore = hasMore
            };
            response.Organizations.AddRange(items.Select(ToProto));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing children for organization {OrgId}", orgId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to list children."));
        }
    }

    [NoAudit("read-only query, not auditable")]
    public override async Task<OrganizationTreeNode> GetOrganizationTree(GetOrganizationTreeRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.OrgId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "OrgId is required."));

        try
        {
            var root = await _repository.GetAsync(request.OrgId, context.CancellationToken)
                ?? throw new RpcException(new Status(StatusCode.NotFound, $"Organization '{request.OrgId}' not found."));

            return await BuildTreeNodeAsync(root, context.CancellationToken);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building organization tree for {OrgId}", request.OrgId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to get organization tree."));
        }
    }

    private async Task<OrganizationTreeNode> BuildTreeNodeAsync(OrganizationDocument doc, CancellationToken cancellationToken)
    {
        var node = new OrganizationTreeNode
        {
            OrgId = doc.OrgId,
            Name = doc.Name,
            Level = doc.Level,
            ManagerId = doc.ManagerId
        };

        var children = await _repository.GetChildrenAsync(doc.OrgId, cancellationToken);
        foreach (var child in children)
        {
            var childNode = await BuildTreeNodeAsync(child, cancellationToken);
            node.Children.Add(childNode);
        }

        return node;
    }

    private async Task CheckCircularReferenceAsync(string orgId, string newParentOrgId, CancellationToken cancellationToken)
    {
        var currentId = newParentOrgId;
        var visited = new HashSet<string> { orgId };

        while (!string.IsNullOrEmpty(currentId))
        {
            if (!visited.Add(currentId))
            {
                throw new RpcException(new Status(
                    StatusCode.FailedPrecondition,
                    $"Moving organization '{orgId}' under '{newParentOrgId}' would create a circular reference."));
            }

            var ancestor = await _repository.GetAsync(currentId, cancellationToken);
            if (ancestor is null)
                break;

            currentId = ancestor.ParentOrgId;
        }
    }

    private static Organization ToProto(OrganizationDocument doc) => new()
    {
        OrgId = doc.OrgId,
        Name = doc.Name,
        ParentOrgId = doc.ParentOrgId,
        Level = doc.Level,
        ManagerId = doc.ManagerId,
        Description = doc.Description
    };
}
