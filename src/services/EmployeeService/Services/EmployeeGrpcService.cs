using EmployeeService.Models;
using EmployeeService.Repositories;
using Grpc.Core;
using HRSystem.Protos.Employee.V1;
using HRSystem.Shared.Audit;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System.Net;

namespace EmployeeService.Services;

public class EmployeeGrpcService : HRSystem.Protos.Employee.V1.EmployeeService.EmployeeServiceBase
{
    private readonly IEmployeeRepository _repository;
    private readonly ILogger<EmployeeGrpcService> _logger;

    public EmployeeGrpcService(IEmployeeRepository repository, ILogger<EmployeeGrpcService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [Audit(AuditEventType.EmployeeCreated)]
    public override async Task<Employee> CreateEmployee(CreateEmployeeRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Name is required."));
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Email is required."));

        var document = new EmployeeDocument
        {
            Name = request.Name,
            Email = request.Email,
            DepartmentId = request.DepartmentId,
            Position = request.Position,
            HireDate = request.HireDate,
            Status = "active"
        };

        try
        {
            var created = await _repository.CreateAsync(document, context.CancellationToken);
            return ToProto(created);
        }
        catch (EmailAlreadyExistsException ex)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, ex.Message));
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos error creating employee");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to create employee."));
        }
    }

    [NoAudit("read-only query, not auditable")]
    public override async Task<Employee> GetEmployee(GetEmployeeRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "EmployeeId is required."));

        try
        {
            var document = await _repository.GetAsync(request.EmployeeId, context.CancellationToken);
            if (document is null)
                throw new RpcException(new Status(StatusCode.NotFound, $"Employee '{request.EmployeeId}' not found."));

            return ToProto(document);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employee {EmployeeId}", request.EmployeeId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to get employee."));
        }
    }

    [Audit(AuditEventType.EmployeeUpdated)]
    public override async Task<Employee> UpdateEmployee(UpdateEmployeeRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "EmployeeId is required."));

        try
        {
            // Read current document with ETag
            var existing = await _repository.GetAsync(request.EmployeeId, context.CancellationToken);
            if (existing is null)
                throw new RpcException(new Status(StatusCode.NotFound, $"Employee '{request.EmployeeId}' not found."));

            // Apply partial updates
            if (request.HasName) existing.Name = request.Name;
            if (request.HasEmail) existing.Email = request.Email;
            if (request.HasDepartmentId) existing.DepartmentId = request.DepartmentId;
            if (request.HasPosition) existing.Position = request.Position;
            if (request.HasStatus) existing.Status = request.Status;

            // Use empty etag to let repository handle concurrency
            var updated = await _repository.UpdateAsync(existing, string.Empty, context.CancellationToken);
            return ToProto(updated);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new RpcException(new Status(StatusCode.Aborted, "Concurrent modification detected. Please retry."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating employee {EmployeeId}", request.EmployeeId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to update employee."));
        }
    }

    [Audit(AuditEventType.EmployeeDeleted)]
    public override async Task<Employee> DeleteEmployee(DeleteEmployeeRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "EmployeeId is required."));

        try
        {
            var document = await _repository.DeleteAsync(request.EmployeeId, context.CancellationToken);
            return ToProto(document);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Employee '{request.EmployeeId}' not found."));
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting employee {EmployeeId}", request.EmployeeId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to delete employee."));
        }
    }

    [NoAudit("read-only query, not auditable")]
    public override async Task<ListEmployeesResponse> ListEmployees(ListEmployeesRequest request, ServerCallContext context)
    {
        var limit = request.Limit > 0 ? request.Limit : 20;
        var cursor = string.IsNullOrEmpty(request.Cursor) ? null : request.Cursor;
        var status = request.HasStatus ? request.Status : null;
        var departmentId = request.HasDepartmentId ? request.DepartmentId : null;

        try
        {
            var (items, continuationToken, hasMore) = await _repository.ListAsync(
                limit, cursor, status, departmentId, context.CancellationToken);

            var response = new ListEmployeesResponse
            {
                NextCursor = continuationToken ?? string.Empty,
                HasMore = hasMore
            };
            response.Employees.AddRange(items.Select(ToProto));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing employees");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to list employees."));
        }
    }

    [NoAudit("read-only existence check, not auditable")]
    public override async Task<HasEmployeesByDepartmentResponse> HasEmployeesByDepartment(
        HasEmployeesByDepartmentRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.DepartmentId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "DepartmentId is required."));

        try
        {
            var hasEmployees = await _repository.HasByDepartmentAsync(request.DepartmentId, context.CancellationToken);
            return new HasEmployeesByDepartmentResponse { HasEmployees = hasEmployees };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking employees for department {DepartmentId}", request.DepartmentId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to check department employees."));
        }
    }

    private static Employee ToProto(EmployeeDocument doc) => new()
    {
        EmployeeId = doc.EmployeeId,
        Name = doc.Name,
        Email = doc.Email,
        DepartmentId = doc.DepartmentId,
        Position = doc.Position,
        HireDate = doc.HireDate,
        Status = doc.Status
    };
}
