namespace HRSystem.Shared.Audit;

/// <summary>
/// Placeholder payload used by the W2 interceptor skeleton when emitting an audit event.
/// W3 replaces this with domain-specific before/after summaries (see
/// docs/features/audit-log.md §IAuditWriter and §payload allow-list).
/// </summary>
public sealed record EmptyAuditPayload : IAuditPayload
{
    public static readonly EmptyAuditPayload Instance = new();
}
