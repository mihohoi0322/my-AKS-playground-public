namespace HRSystem.Shared.Audit;

/// <summary>
/// CloudEvents <c>type</c> string constants used by HRSystem audit events.
/// Naming follows <c>hrsystem.&lt;domain&gt;.&lt;action&gt;.v&lt;N&gt;</c>
/// (see docs/features/audit-log.md, CloudEvents schema versioning strategy).
/// Phase 1 ships v1 only; subsequent versions live alongside until deprecation.
/// </summary>
public static class AuditEventType
{
    // Employee domain
    public const string EmployeeCreated = "hrsystem.employee.created.v1";
    public const string EmployeeUpdated = "hrsystem.employee.updated.v1";
    public const string EmployeeDeleted = "hrsystem.employee.deleted.v1";

    // Attendance domain
    public const string AttendanceClockedIn = "hrsystem.attendance.clocked_in.v1";
    public const string AttendanceClockedOut = "hrsystem.attendance.clocked_out.v1";
    public const string AttendanceApproved = "hrsystem.attendance.approved.v1";

    // Organization domain
    public const string OrganizationCreated = "hrsystem.organization.created.v1";
    public const string OrganizationChanged = "hrsystem.organization.changed.v1";
    public const string OrganizationDeleted = "hrsystem.organization.deleted.v1";

    // Delegation domain (ADR-012)
    public const string DelegationCreated = "hrsystem.delegation.granted.v1";
    public const string DelegationRevoked = "hrsystem.delegation.revoked.v1";

    // Delegation guards (ADR-012 amendment: transitive delegation forbidden)
    public const string DelegationSelfGrantBlocked = "hrsystem.delegation.self_grant_blocked.v1";
    public const string DelegationTransitiveAttemptBlocked = "hrsystem.delegation.transitive_attempt_blocked.v1";
    public const string DelegationUnauthorizedActingAsBlocked = "hrsystem.delegation.unauthorized_acting_as_blocked.v1";
    public const string DelegationPrivilegedRoleBlocked = "hrsystem.delegation.privileged_role_blocked.v1";

    // Audit-of-audit (T-06)
    public const string AuditViewAttempt = "hrsystem.audit.read.v1";

    // Operational
    public const string RateLimited = "hrsystem.audit.rate_limited.v1";
}
