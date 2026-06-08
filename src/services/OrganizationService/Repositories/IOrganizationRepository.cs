using OrganizationService.Models;

namespace OrganizationService.Repositories;

public interface IOrganizationRepository
{
    Task<OrganizationDocument> CreateAsync(OrganizationDocument document, CancellationToken cancellationToken = default);
    Task<OrganizationDocument?> GetAsync(string orgId, CancellationToken cancellationToken = default);
    Task<OrganizationDocument> UpdateAsync(OrganizationDocument document, CancellationToken cancellationToken = default);
    /// <summary>
    /// Physically deletes an organization. Returns <c>true</c> when the document existed and
    /// was removed; <c>false</c> when the document was already absent (so callers can decide
    /// between idempotent success and NOT_FOUND semantics).
    /// </summary>
    Task<bool> DeleteAsync(string orgId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<OrganizationDocument> Items, string? ContinuationToken, bool HasMore)> ListChildrenAsync(
        string parentOrgId,
        int limit,
        string? cursor = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationDocument>> GetChildrenAsync(string parentOrgId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Lightweight existence check for child organizations. Returns <c>true</c> if at least one
    /// organization has <paramref name="parentOrgId"/> as its parent. Designed to minimise RU
    /// consumption (Cosmos uses <c>SELECT VALUE 1 ... OFFSET 0 LIMIT 1</c>) for delete preconditions.
    /// </summary>
    Task<bool> HasChildrenAsync(string parentOrgId, CancellationToken cancellationToken = default);
}
