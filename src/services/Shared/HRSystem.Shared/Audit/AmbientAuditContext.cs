namespace HRSystem.Shared.Audit;

/// <summary>
/// Identity captured at the request boundary for audit attribution.
/// </summary>
/// <param name="ObjectId">Stable identifier (e.g. Entra ID object id).</param>
/// <param name="ActorType">Actor kind (user / service / system).</param>
public sealed record AuditActor(string ObjectId, string ActorType);

/// <summary>
/// Snapshot of the delegation policy that authorised an <c>actingAs</c> assertion (M-S2).
/// </summary>
public sealed record DelegationPolicySnapshot(string Version, string Hash);

/// <summary>
/// Carrier for actor / actingAs / delegation snapshot across an async call chain
/// (per docs/features/audit-log.md §AmbientAuditContext, ADR-012).
/// </summary>
public sealed record AuditAmbient(
    AuditActor Actor,
    AuditActor? ActingAs,
    DelegationPolicySnapshot? DelegationPolicySnapshot,
    string? ClientIpHash,
    string? UserAgent,
    string? Traceparent);

/// <summary>
/// AsyncLocal-backed accessor for the current request's audit context.
/// Middleware / gRPC interceptors set this at the boundary; service code reads only.
/// </summary>
public static class AmbientAuditContext
{
    private static readonly AsyncLocal<AuditAmbient?> _current = new();

    /// <summary>
    /// Currently-bound ambient, or <c>null</c> outside a request scope.
    /// </summary>
    public static AuditAmbient? Current => _current.Value;

    /// <summary>
    /// Replace the ambient. Returns an <see cref="IDisposable"/> that restores the previous value
    /// on dispose, allowing nested scopes (e.g. tests, batch loops).
    /// </summary>
    public static IDisposable Push(AuditAmbient ambient)
    {
        ArgumentNullException.ThrowIfNull(ambient);
        var previous = _current.Value;
        _current.Value = ambient;
        return new Scope(previous);
    }

    /// <summary>
    /// Resolve the ambient or throw if absent (used by writers that require attribution).
    /// </summary>
    public static AuditAmbient Require()
    {
        return _current.Value
            ?? throw new InvalidOperationException(
                "AmbientAuditContext is not set. " +
                "Ensure the request middleware / gRPC interceptor pushed the context before invoking IAuditWriter.");
    }

    private sealed class Scope : IDisposable
    {
        private readonly AuditAmbient? _previous;
        private bool _disposed;

        public Scope(AuditAmbient? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
