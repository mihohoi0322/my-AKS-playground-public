namespace HRSystem.Shared.Audit;

/// <summary>
/// High-level action categorisation for audit events.
/// Maps onto CloudEvents <c>data.action</c>.
/// </summary>
public enum AuditAction
{
    Unknown = 0,
    Create,
    Read,
    Update,
    Delete,
    Approve,
    Reject,
    Grant,
    Revoke,
}

/// <summary>
/// Outcome of the audited operation.
/// </summary>
public enum AuditResult
{
    Unknown = 0,
    Success,
    Failure,
    Denied,
    RateLimited,
}

/// <summary>
/// Severity / classification used for fail-mode selection (fail-closed vs best-effort)
/// and SoD-sensitive routing.
/// </summary>
public enum AuditClassification
{
    Unknown = 0,
    MutationHigh,
    MutationMedium,
    ReadHigh,
    ReadNormal,
}
