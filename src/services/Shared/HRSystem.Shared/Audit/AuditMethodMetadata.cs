using System.Reflection;

namespace HRSystem.Shared.Audit;

/// <summary>
/// Resolved audit metadata for a single gRPC RPC method. Built once at startup by
/// <see cref="AuditAttributeValidator"/> and cached in <see cref="AuditMethodRegistry"/>.
/// </summary>
public sealed record AuditMethodMetadata(
    string GrpcPath,
    string GrpcServiceName,
    string MethodName,
    MethodInfo Method,
    AuditAttribute? Audit,
    NoAuditAttribute? NoAudit)
{
    /// <summary>True when the method is annotated with <c>[NoAudit]</c>.</summary>
    public bool Skipped => NoAudit is not null;
}
