using Microsoft.Extensions.Logging;

namespace HRSystem.Shared.Audit;

/// <summary>
/// No-op <see cref="IAuditWriter"/> used as the default registration until the W3 ingestion
/// implementation lands. Emits an OTel span (so trace plumbing can be exercised) and logs
/// at debug level. Never throws; never persists. Production wiring MUST replace this with
/// the Cosmos-backed writer (fail-closed for mutation-class events).
/// </summary>
public sealed class NoopAuditWriter : IAuditWriter
{
    private readonly ILogger<NoopAuditWriter>? _logger;

    public NoopAuditWriter()
    {
    }

    public NoopAuditWriter(ILogger<NoopAuditWriter> logger)
    {
        _logger = logger;
    }

    public Task WriteAsync<TPayload>(
        AuditEventDescriptor<TPayload> descriptor,
        CancellationToken cancellationToken)
        where TPayload : class, IAuditPayload
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = AuditMetrics.ActivitySource.StartActivity(
            "audit.write",
            System.Diagnostics.ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("audit.event.type", descriptor.Type);
            activity.SetTag("audit.resource.type", descriptor.ResourceType);
            activity.SetTag("audit.resource.id", descriptor.ResourceId);
            activity.SetTag("audit.action", descriptor.Action.ToString());
            activity.SetTag("audit.classification", descriptor.Classification.ToString());
            activity.SetTag("audit.writer", "noop");
        }

        _logger?.LogDebug(
            "NoopAuditWriter received event Type={Type} Resource={ResourceType}/{ResourceId}",
            descriptor.Type,
            descriptor.ResourceType,
            descriptor.ResourceId);

        return Task.CompletedTask;
    }
}
