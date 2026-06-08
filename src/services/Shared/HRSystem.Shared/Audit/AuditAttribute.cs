namespace HRSystem.Shared.Audit;

/// <summary>
/// Marks a gRPC service method as auditable. The interceptor emits an audit event with the
/// supplied <see cref="EventType"/> (a <see cref="AuditEventType"/> constant) on successful
/// completion. See docs/features/audit-log.md and logs/discussion/2026-04-26-w2-design-decisions.md.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuditAttribute : Attribute
{
    public AuditAttribute(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("eventType is required.", nameof(eventType));
        }
        EventType = eventType;
    }

    /// <summary>CloudEvents <c>type</c> string (e.g. <see cref="AuditEventType.EmployeeUpdated"/>).</summary>
    public string EventType { get; }
}

/// <summary>
/// Explicitly opts a gRPC service method out of audit emission. A <see cref="Reason"/> is
/// required so the rationale is reviewable in code search and PR diffs (typically read-only
/// queries). Methods missing both <c>[Audit]</c> and <c>[NoAudit]</c> fail the Roslyn analyzer
/// (HRSAUD001) and the startup reflection scan.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class NoAuditAttribute : Attribute
{
    public NoAuditAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("reason is required.", nameof(reason));
        }
        Reason = reason;
    }

    /// <summary>Human-readable rationale (must be non-empty).</summary>
    public string Reason { get; }
}
