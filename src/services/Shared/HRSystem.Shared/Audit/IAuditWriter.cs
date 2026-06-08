namespace HRSystem.Shared.Audit;

/// <summary>
/// Minimal audit-write contract per docs/features/audit-log.md §"IAuditWriter 最小契約 (M-6)".
/// Implementations MUST resolve <c>actor</c> / <c>actingAs</c> from
/// <see cref="AmbientAuditContext"/>; callers cannot supply them, by design (T-09).
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Persist a single audit event. The fail-mode (fail-closed vs best-effort) is determined
    /// by <see cref="AuditEventDescriptor{TPayload}.Classification"/>.
    /// </summary>
    Task WriteAsync<TPayload>(
        AuditEventDescriptor<TPayload> descriptor,
        CancellationToken cancellationToken)
        where TPayload : class, IAuditPayload;
}
