using System.Net;
using EmployeeService.Models;
using HRSystem.Shared.Cosmos;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace EmployeeService.Repositories;

public class CosmosEmployeeRepository : IEmployeeRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosEmployeeRepository> _logger;

    public CosmosEmployeeRepository(ICosmosClientFactory cosmosClientFactory, CosmosSettings cosmosSettings, ILogger<CosmosEmployeeRepository> logger)
    {
        _logger = logger;
        var client = cosmosClientFactory.CreateClient();
        _container = client.GetContainer(cosmosSettings.DatabaseName, "employees");
    }

    public async Task<EmployeeDocument> CreateAsync(EmployeeDocument document, CancellationToken cancellationToken = default)
    {
        document.Id = Guid.NewGuid().ToString();
        document.EmployeeId = document.Id;

        // Check email uniqueness
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.email = @email")
            .WithParameter("@email", document.Email);

        using var iterator = _container.GetItemQueryIterator<int>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            if (response.FirstOrDefault() > 0)
            {
                throw new EmailAlreadyExistsException(document.Email);
            }
        }

        var result = await _container.CreateItemAsync(document, new PartitionKey(document.EmployeeId), cancellationToken: cancellationToken);
        _logger.LogInformation("Created employee {EmployeeId}", result.Resource.EmployeeId);
        return result.Resource;
    }

    public async Task<EmployeeDocument?> GetAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<EmployeeDocument>(employeeId, new PartitionKey(employeeId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<EmployeeDocument> UpdateAsync(EmployeeDocument document, string etag, CancellationToken cancellationToken = default)
    {
        var options = new ItemRequestOptions { IfMatchEtag = etag };
        var response = await _container.ReplaceItemAsync(document, document.EmployeeId, new PartitionKey(document.EmployeeId), options, cancellationToken);
        _logger.LogInformation("Updated employee {EmployeeId}", document.EmployeeId);
        return response.Resource;
    }

    public async Task<EmployeeDocument> DeleteAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        var response = await _container.ReadItemAsync<EmployeeDocument>(employeeId, new PartitionKey(employeeId), cancellationToken: cancellationToken);
        var document = response.Resource;
        document.Status = "inactive";

        var options = new ItemRequestOptions { IfMatchEtag = response.ETag };
        var result = await _container.ReplaceItemAsync(document, employeeId, new PartitionKey(employeeId), options, cancellationToken);
        _logger.LogInformation("Soft-deleted employee {EmployeeId}", employeeId);
        return result.Resource;
    }

    public async Task<(IReadOnlyList<EmployeeDocument> Items, string? ContinuationToken, bool HasMore)> ListAsync(
        int limit,
        string? cursor = null,
        string? status = null,
        string? departmentId = null,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string> { "1=1" };
        var queryDef = new QueryDefinition(string.Empty);

        if (!string.IsNullOrEmpty(status))
        {
            conditions.Add("c.status = @status");
            queryDef = queryDef.WithParameter("@status", status);
        }

        if (!string.IsNullOrEmpty(departmentId))
        {
            conditions.Add("c.departmentId = @departmentId");
            queryDef = queryDef.WithParameter("@departmentId", departmentId);
        }

        var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY c.employeeId";
        queryDef = new QueryDefinition(sql);

        // Re-add parameters after rebuilding query text
        if (!string.IsNullOrEmpty(status))
            queryDef = queryDef.WithParameter("@status", status);
        if (!string.IsNullOrEmpty(departmentId))
            queryDef = queryDef.WithParameter("@departmentId", departmentId);

        var requestOptions = new QueryRequestOptions { MaxItemCount = limit };
        using var iterator = _container.GetItemQueryIterator<EmployeeDocument>(queryDef, cursor, requestOptions);

        var items = new List<EmployeeDocument>();
        string? continuationToken = null;

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            items.AddRange(response);
            continuationToken = response.ContinuationToken;
        }

        return (items, continuationToken, continuationToken is not null);
    }

    public async Task<bool> HasByDepartmentAsync(string departmentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(departmentId))
            return false;

        // Existence check: SELECT VALUE 1 with TOP 1 — minimises RU vs COUNT(1) over a partition
        // and avoids fetching PII payload (docs/db-design.md §6, ADR-007).
        // We exclude logically-deleted (status="inactive") employees so the gate aligns with
        // OrganizationService.DeleteOrganization semantics.
        var query = new QueryDefinition(
                "SELECT VALUE 1 FROM c WHERE c.departmentId = @departmentId AND (NOT IS_DEFINED(c.status) OR c.status != @inactive) OFFSET 0 LIMIT 1")
            .WithParameter("@departmentId", departmentId)
            .WithParameter("@inactive", "inactive");

        var requestOptions = new QueryRequestOptions { MaxItemCount = 1 };
        using var iterator = _container.GetItemQueryIterator<int>(query, requestOptions: requestOptions);
        if (!iterator.HasMoreResults)
            return false;

        var response = await iterator.ReadNextAsync(cancellationToken);
        return response.Count > 0;
    }
}

public class EmailAlreadyExistsException : Exception
{
    public string Email { get; }

    public EmailAlreadyExistsException(string email)
        : base($"An employee with email '{email}' already exists.")
    {
        Email = email;
    }
}
