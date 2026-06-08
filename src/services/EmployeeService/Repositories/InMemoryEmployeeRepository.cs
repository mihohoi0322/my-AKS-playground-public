using System.Collections.Concurrent;
using EmployeeService.Models;

namespace EmployeeService.Repositories;

/// <summary>
/// In-memory repository for local development without Cosmos DB.
/// </summary>
public class InMemoryEmployeeRepository : IEmployeeRepository
{
    private readonly ConcurrentDictionary<string, EmployeeDocument> _store = new();

    public Task<EmployeeDocument> CreateAsync(EmployeeDocument document, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = Guid.NewGuid().ToString();
        if (string.IsNullOrEmpty(document.EmployeeId))
            document.EmployeeId = document.Id;
        _store[document.EmployeeId] = document;
        return Task.FromResult(document);
    }

    public Task<EmployeeDocument?> GetAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(employeeId, out var doc);
        return Task.FromResult(doc);
    }

    public Task<EmployeeDocument> UpdateAsync(EmployeeDocument document, string etag, CancellationToken cancellationToken = default)
    {
        _store[document.EmployeeId] = document;
        return Task.FromResult(document);
    }

    public Task<EmployeeDocument> DeleteAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (_store.TryRemove(employeeId, out var doc))
        {
            doc.Status = "deleted";
            return Task.FromResult(doc);
        }
        throw new KeyNotFoundException($"Employee {employeeId} not found.");
    }

    public Task<(IReadOnlyList<EmployeeDocument> Items, string? ContinuationToken, bool HasMore)> ListAsync(
        int limit,
        string? cursor = null,
        string? status = null,
        string? departmentId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _store.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(e => e.Status == status);
        if (!string.IsNullOrEmpty(departmentId))
            query = query.Where(e => e.DepartmentId == departmentId);

        var ordered = query.OrderBy(e => e.Name).ToList();

        int startIndex = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var idx))
            startIndex = idx;

        var page = ordered.Skip(startIndex).Take(limit).ToList();
        var hasMore = startIndex + limit < ordered.Count;
        string? nextCursor = hasMore ? (startIndex + limit).ToString() : null;

        return Task.FromResult<(IReadOnlyList<EmployeeDocument>, string?, bool)>((page, nextCursor, hasMore));
    }

    public Task<bool> HasByDepartmentAsync(string departmentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(departmentId))
            return Task.FromResult(false);

        var has = _store.Values.Any(e => e.DepartmentId == departmentId && e.Status != "inactive");
        return Task.FromResult(has);
    }
}
