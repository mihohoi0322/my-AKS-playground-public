using System.Collections.Concurrent;
using System.Reflection;

namespace HRSystem.Shared.Audit;

/// <summary>
/// Startup-built lookup of <see cref="AuditMethodMetadata"/> keyed by gRPC method path
/// (<c>/full.service.Name/MethodName</c>) and by <see cref="MethodInfo"/>. Populated by
/// <see cref="AuditAttributeValidator"/> at host build time so the per-call interceptor path
/// performs an O(1) dictionary lookup with no reflection.
/// </summary>
public sealed class AuditMethodRegistry
{
    private readonly IReadOnlyDictionary<string, AuditMethodMetadata> _byPath;
    private readonly ConcurrentDictionary<MethodInfo, AuditMethodMetadata> _byMethod;
    private long _reflectionResolutionCount;

    public AuditMethodRegistry(IEnumerable<AuditMethodMetadata> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToList();
        _byPath = list.ToDictionary(e => e.GrpcPath, e => e, StringComparer.Ordinal);
        _byMethod = new ConcurrentDictionary<MethodInfo, AuditMethodMetadata>(
            list.Select(e => new KeyValuePair<MethodInfo, AuditMethodMetadata>(e.Method, e)));
        _reflectionResolutionCount = list.Count;
    }

    /// <summary>
    /// Total number of MethodInfo entries resolved via reflection. Stable after registration;
    /// used by tests to assert the per-call path does not re-reflect.
    /// </summary>
    public long ReflectionResolutionCount => Interlocked.Read(ref _reflectionResolutionCount);

    /// <summary>Number of distinct RPC methods tracked.</summary>
    public int Count => _byPath.Count;

    /// <summary>Lookup by gRPC method path (<c>/svc/Method</c>).</summary>
    public bool TryGet(string grpcPath, out AuditMethodMetadata metadata)
    {
        if (string.IsNullOrEmpty(grpcPath))
        {
            metadata = null!;
            return false;
        }
        return _byPath.TryGetValue(grpcPath, out metadata!);
    }

    /// <summary>Lookup by <see cref="MethodInfo"/> (cache hit only — no reflection performed).</summary>
    public bool TryGet(MethodInfo method, out AuditMethodMetadata metadata)
    {
        if (method is null)
        {
            metadata = null!;
            return false;
        }
        return _byMethod.TryGetValue(method, out metadata!);
    }

    /// <summary>Snapshot of all entries (test/debug only).</summary>
    public IReadOnlyCollection<AuditMethodMetadata> Entries => _byPath.Values.ToArray();
}
