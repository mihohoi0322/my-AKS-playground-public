using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace HRSystem.Shared.Audit.Interceptors;

/// <summary>
/// gRPC server interceptor that establishes the per-request <see cref="AmbientAuditContext"/>
/// from gateway-propagated headers and emits a placeholder audit event for every method
/// annotated with <c>[Audit]</c>. Methods annotated with <c>[NoAudit("reason")]</c> are
/// counted into <see cref="AuditMetrics.SkippedCount"/> and short-circuit emit.
/// </summary>
/// <remarks>
/// W2 scope: skeleton only. The descriptor's payload is <see cref="EmptyAuditPayload"/>; the
/// real before/after summaries land in W3 (per logs/discussion/2026-04-26-w2-design-decisions.md).
/// All four RPC shapes (unary / server-streaming / client-streaming / duplex) are wrapped with
/// the same Push/Restore + emit pattern so future business logic does not need to revisit them.
/// </remarks>
public sealed class AuditServerInterceptor : Interceptor
{
    private readonly IAuditWriter _writer;
    private readonly AuditMethodRegistry _registry;
    private readonly ILogger<AuditServerInterceptor> _logger;

    public AuditServerInterceptor(
        IAuditWriter writer,
        AuditMethodRegistry registry,
        ILogger<AuditServerInterceptor> logger)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var meta = ResolveMetadataOrThrow(context);
        using var ambient = PushAmbient(context);
        return await InvokeAndAuditAsync(meta, context, () => continuation(request, context)).ConfigureAwait(false);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var meta = ResolveMetadataOrThrow(context);
        using var ambient = PushAmbient(context);
        return await InvokeAndAuditAsync(meta, context, () => continuation(requestStream, context)).ConfigureAwait(false);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var meta = ResolveMetadataOrThrow(context);
        using var ambient = PushAmbient(context);
        await InvokeAndAuditAsync<object?>(meta, context, async () =>
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
            return null;
        }).ConfigureAwait(false);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var meta = ResolveMetadataOrThrow(context);
        using var ambient = PushAmbient(context);
        await InvokeAndAuditAsync<object?>(meta, context, async () =>
        {
            await continuation(requestStream, responseStream, context).ConfigureAwait(false);
            return null;
        }).ConfigureAwait(false);
    }

    /// <summary>Test seam: resolve metadata directly from a gRPC method path.</summary>
    internal AuditMethodMetadata ResolveMetadataOrThrow(string grpcPath)
    {
        if (!_registry.TryGet(grpcPath, out var meta))
        {
            // Defence-in-depth: this should have been caught by AuditAttributeValidator at startup
            // and HRSAUD001 at compile time. Reaching here indicates a registry/path mismatch.
            throw new InvalidOperationException(
                $"No audit metadata registered for '{grpcPath}'. " +
                "Ensure AddAuditAttributeValidation was called with the assembly that declares this RPC method.");
        }
        return meta;
    }

    private AuditMethodMetadata ResolveMetadataOrThrow(ServerCallContext context)
    {
        return ResolveMetadataOrThrow(context.Method);
    }

    private static IDisposable PushAmbient(ServerCallContext context)
    {
        var headers = context.RequestHeaders;
        var actorOid = GetHeader(headers, AuditHeaders.ActorOid) ?? "anonymous";
        var actorType = GetHeader(headers, AuditHeaders.ActorType) ?? "user";
        var actingAsOid = GetHeader(headers, AuditHeaders.ActingAs);
        var actingAsType = GetHeader(headers, AuditHeaders.ActingAsType) ?? "user";
        var clientIpHash = GetHeader(headers, AuditHeaders.ClientIpHash);
        var userAgent = GetHeader(headers, AuditHeaders.UserAgent);
        var traceparent = GetHeader(headers, "traceparent");

        var actor = new AuditActor(actorOid, actorType);
        var actingAs = string.IsNullOrEmpty(actingAsOid) ? null : new AuditActor(actingAsOid, actingAsType);

        var ambient = new AuditAmbient(
            Actor: actor,
            ActingAs: actingAs,
            DelegationPolicySnapshot: null,
            ClientIpHash: clientIpHash,
            UserAgent: userAgent,
            Traceparent: traceparent);

        return AmbientAuditContext.Push(ambient);
    }

    private static string? GetHeader(Metadata? headers, string key)
    {
        if (headers is null)
        {
            return null;
        }
        for (var i = 0; i < headers.Count; i++)
        {
            var entry = headers[i];
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase) && !entry.IsBinary)
            {
                return entry.Value;
            }
        }
        return null;
    }

    private async Task<T> InvokeAndAuditAsync<T>(
        AuditMethodMetadata meta,
        ServerCallContext context,
        Func<Task<T>> continuation)
    {
        if (meta.Skipped)
        {
            AuditMetrics.SkippedCount.Add(
                1,
                new KeyValuePair<string, object?>("rpc.method", meta.GrpcPath),
                new KeyValuePair<string, object?>("reason", meta.NoAudit!.Reason));
            return await continuation().ConfigureAwait(false);
        }

        if (meta.Audit is null)
        {
            // Should be unreachable: validator ensures exactly one of Audit/NoAudit is set.
            throw new InvalidOperationException(
                $"AuditMethodMetadata for '{meta.GrpcPath}' has neither [Audit] nor [NoAudit].");
        }

        using var activity = AuditMetrics.ActivitySource.StartActivity(
            "audit.intercept",
            ActivityKind.Server);
        activity?.SetTag("rpc.method", meta.GrpcPath);
        activity?.SetTag("audit.event.type", meta.Audit.EventType);

        var stopwatch = Stopwatch.StartNew();
        T response;
        try
        {
            response = await continuation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }

        try
        {
            // W2 skeleton: emit a placeholder descriptor. W3 will inject real before/after
            // summaries via a domain-side hook (see docs/features/audit-log.md §W3).
            var descriptor = new AuditEventDescriptor<EmptyAuditPayload>(
                Type: meta.Audit.EventType,
                ResourceType: meta.GrpcServiceName,
                ResourceId: "(skeleton)",
                Action: AuditAction.Unknown,
                Result: AuditResult.Success,
                Classification: AuditClassification.Unknown,
                BeforeSummary: null,
                AfterSummary: null);

            await _writer.WriteAsync(descriptor, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AuditMetrics.WriteFailures.Add(
                1,
                new KeyValuePair<string, object?>("rpc.method", meta.GrpcPath),
                new KeyValuePair<string, object?>("audit.event.type", meta.Audit.EventType));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Audit write failed for {Method}", meta.GrpcPath);

            // W3 will route fail-closed (mutation-class) vs best-effort decisions through
            // descriptor.Classification; the skeleton swallows the error so the RPC response is
            // not affected by the placeholder writer.
        }
        finally
        {
            stopwatch.Stop();
            AuditMetrics.WriteDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("rpc.method", meta.GrpcPath));
        }

        return response;
    }
}
