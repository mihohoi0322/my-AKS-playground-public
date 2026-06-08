namespace HRSystem.Shared.Audit;

/// <summary>
/// Marker interface for audit event payloads. Domain models (e.g. before/after summaries)
/// implement this to be admissible into <see cref="AuditEventDescriptor{TPayload}"/>.
/// </summary>
public interface IAuditPayload
{
}
