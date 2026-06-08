namespace HRSystem.Shared.Audit;

/// <summary>
/// Describes a single audit event submitted by a domain service via <see cref="IAuditWriter"/>.
/// Contract intentionally excludes <c>actor</c> / <c>actingAs</c>: those are read from
/// <see cref="AmbientAuditContext"/> by the writer (see docs/features/audit-log.md
/// "IAuditWriter 最小契約 (M-6)").
/// </summary>
/// <typeparam name="TPayload">Domain summary type implementing <see cref="IAuditPayload"/>.</typeparam>
/// <param name="Type">CloudEvents <c>type</c> (e.g. <see cref="AuditEventType.DelegationCreated"/>).</param>
/// <param name="ResourceType">Resource kind (e.g. "employee", "delegation").</param>
/// <param name="ResourceId">Resource identifier; combined with <paramref name="ResourceType"/> to
/// form the CloudEvents <c>subject</c>.</param>
/// <param name="Action">Operation classification.</param>
/// <param name="Result">Operation outcome.</param>
/// <param name="Classification">Fail-mode selector.</param>
/// <param name="BeforeSummary">Domain state before the operation (null for creates).</param>
/// <param name="AfterSummary">Domain state after the operation (null for deletes).</param>
public sealed record AuditEventDescriptor<TPayload>(
    string Type,
    string ResourceType,
    string ResourceId,
    AuditAction Action,
    AuditResult Result,
    AuditClassification Classification,
    TPayload? BeforeSummary,
    TPayload? AfterSummary)
    where TPayload : class, IAuditPayload;
