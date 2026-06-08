using System.Collections.Concurrent;
using OrganizationService.Models;

namespace OrganizationService.Repositories;

/// <summary>
/// In-memory repository for local development without Cosmos DB.
/// </summary>
public class InMemoryOrganizationRepository : IOrganizationRepository
{
    private readonly ConcurrentDictionary<string, OrganizationDocument> _store = new();

    public Task<OrganizationDocument> CreateAsync(OrganizationDocument document, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = Guid.NewGuid().ToString();
        if (string.IsNullOrEmpty(document.OrgId))
            document.OrgId = document.Id;
        _store[document.OrgId] = document;
        return Task.FromResult(document);
    }

    public Task<OrganizationDocument?> GetAsync(string orgId, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(orgId, out var doc);
        return Task.FromResult(doc);
    }

    public Task<OrganizationDocument> UpdateAsync(OrganizationDocument document, CancellationToken cancellationToken = default)
    {
        _store[document.OrgId] = document;
        return Task.FromResult(document);
    }

    public Task<bool> DeleteAsync(string orgId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_store.TryRemove(orgId, out _));
    }

    public Task<(IReadOnlyList<OrganizationDocument> Items, string? ContinuationToken, bool HasMore)> ListChildrenAsync(
        string parentOrgId,
        int limit,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        // When parentOrgId is empty, return all organizations (dev convenience)
        var query = string.IsNullOrEmpty(parentOrgId)
            ? _store.Values.OrderBy(o => o.Level).ThenBy(o => o.Name).ToList()
            : _store.Values.Where(o => o.ParentOrgId == parentOrgId).OrderBy(o => o.Name).ToList();

        int startIndex = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var idx))
            startIndex = idx;

        var page = query.Skip(startIndex).Take(limit).ToList();
        var hasMore = startIndex + limit < query.Count;
        string? nextCursor = hasMore ? (startIndex + limit).ToString() : null;

        return Task.FromResult<(IReadOnlyList<OrganizationDocument>, string?, bool)>((page, nextCursor, hasMore));
    }

    public Task<IReadOnlyList<OrganizationDocument>> GetChildrenAsync(string parentOrgId, CancellationToken cancellationToken = default)
    {
        var children = _store.Values
            .Where(o => o.ParentOrgId == parentOrgId)
            .OrderBy(o => o.Name)
            .ToList();
        return Task.FromResult<IReadOnlyList<OrganizationDocument>>(children);
    }

    public Task<bool> HasChildrenAsync(string parentOrgId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(parentOrgId))
            return Task.FromResult(false);

        var has = _store.Values.Any(o => o.ParentOrgId == parentOrgId);
        return Task.FromResult(has);
    }
}
