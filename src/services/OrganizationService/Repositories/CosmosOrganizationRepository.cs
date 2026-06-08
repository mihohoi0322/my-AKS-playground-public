using System.Net;
using OrganizationService.Models;
using HRSystem.Shared.Cosmos;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace OrganizationService.Repositories;

public class CosmosOrganizationRepository : IOrganizationRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosOrganizationRepository> _logger;

    public CosmosOrganizationRepository(
        ICosmosClientFactory cosmosClientFactory,
        CosmosSettings cosmosSettings,
        ILogger<CosmosOrganizationRepository> logger)
    {
        _logger = logger;
        var client = cosmosClientFactory.CreateClient();
        _container = client.GetContainer(cosmosSettings.DatabaseName, "organizations");
    }

    public async Task<OrganizationDocument> CreateAsync(OrganizationDocument document, CancellationToken cancellationToken = default)
    {
        document.Id = Guid.NewGuid().ToString();
        document.OrgId = document.Id;

        if (!string.IsNullOrEmpty(document.ParentOrgId))
        {
            var parent = await GetAsync(document.ParentOrgId, cancellationToken)
                ?? throw new ParentOrganizationNotFoundException(document.ParentOrgId);
            document.Level = parent.Level + 1;
        }
        else
        {
            document.Level = 0;
        }

        var result = await _container.CreateItemAsync(document, new PartitionKey(document.OrgId), cancellationToken: cancellationToken);
        _logger.LogInformation("Created organization {OrgId} at level {Level}", result.Resource.OrgId, result.Resource.Level);
        return result.Resource;
    }

    public async Task<OrganizationDocument?> GetAsync(string orgId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<OrganizationDocument>(orgId, new PartitionKey(orgId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<OrganizationDocument> UpdateAsync(OrganizationDocument document, CancellationToken cancellationToken = default)
    {
        var response = await _container.ReplaceItemAsync(document, document.OrgId, new PartitionKey(document.OrgId), cancellationToken: cancellationToken);
        _logger.LogInformation("Updated organization {OrgId}", document.OrgId);
        return response.Resource;
    }

    public async Task<bool> DeleteAsync(string orgId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.DeleteItemAsync<OrganizationDocument>(orgId, new PartitionKey(orgId), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted organization {OrgId}", orgId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<(IReadOnlyList<OrganizationDocument> Items, string? ContinuationToken, bool HasMore)> ListChildrenAsync(
        string parentOrgId,
        int limit,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var queryDef = new QueryDefinition("SELECT * FROM c WHERE c.parentOrgId = @parentOrgId ORDER BY c.name")
            .WithParameter("@parentOrgId", parentOrgId);

        var requestOptions = new QueryRequestOptions { MaxItemCount = limit };
        using var iterator = _container.GetItemQueryIterator<OrganizationDocument>(queryDef, cursor, requestOptions);

        var items = new List<OrganizationDocument>();
        string? continuationToken = null;

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            items.AddRange(response);
            continuationToken = response.ContinuationToken;
        }

        return (items, continuationToken, continuationToken is not null);
    }

    public async Task<IReadOnlyList<OrganizationDocument>> GetChildrenAsync(string parentOrgId, CancellationToken cancellationToken = default)
    {
        var queryDef = new QueryDefinition("SELECT * FROM c WHERE c.parentOrgId = @parentOrgId ORDER BY c.name")
            .WithParameter("@parentOrgId", parentOrgId);

        using var iterator = _container.GetItemQueryIterator<OrganizationDocument>(queryDef);
        var results = new List<OrganizationDocument>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task<bool> HasChildrenAsync(string parentOrgId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(parentOrgId))
            return false;

        // Existence check: SELECT VALUE 1 with OFFSET 0 LIMIT 1 — minimises RU vs fetching all
        // children just to count (mirrors EmployeeService.HasByDepartmentAsync, docs/db-design.md §6).
        var queryDef = new QueryDefinition(
                "SELECT VALUE 1 FROM c WHERE c.parentOrgId = @parentOrgId OFFSET 0 LIMIT 1")
            .WithParameter("@parentOrgId", parentOrgId);

        var requestOptions = new QueryRequestOptions { MaxItemCount = 1 };
        using var iterator = _container.GetItemQueryIterator<int>(queryDef, requestOptions: requestOptions);
        if (!iterator.HasMoreResults)
            return false;

        var response = await iterator.ReadNextAsync(cancellationToken);
        return response.Count > 0;
    }
}

public class ParentOrganizationNotFoundException : Exception
{
    public string ParentOrgId { get; }

    public ParentOrganizationNotFoundException(string parentOrgId)
        : base($"Parent organization '{parentOrgId}' not found.")
    {
        ParentOrgId = parentOrgId;
    }
}

public class CircularReferenceException : Exception
{
    public string OrgId { get; }
    public string NewParentOrgId { get; }

    public CircularReferenceException(string orgId, string newParentOrgId)
        : base($"Moving organization '{orgId}' under '{newParentOrgId}' would create a circular reference.")
    {
        OrgId = orgId;
        NewParentOrgId = newParentOrgId;
    }
}
