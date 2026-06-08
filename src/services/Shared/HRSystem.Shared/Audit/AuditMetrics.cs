using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HRSystem.Shared.Audit;

/// <summary>
/// OpenTelemetry instrumentation primitives for the audit pipeline.
/// Names are stable and referenced by ServiceDefaults / Application Insights dashboards
/// (docs/features/audit-log.md §OpenTelemetry).
/// </summary>
public static class AuditMetrics
{
    /// <summary>OTel ActivitySource name (also used as Meter name).</summary>
    public const string SourceName = "HRSystem.Audit";

    /// <summary>
    /// ActivitySource for audit-related spans. Business RPC spans become parents of
    /// <c>audit.write</c> spans via W3C Trace Context.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>Meter shared by all audit metrics.</summary>
    public static readonly Meter Meter = new(SourceName);

    // -------- IAuditWriter (W1) --------

    /// <summary>Audit ingestion latency, milliseconds.</summary>
    public static readonly Histogram<double> WriteDuration =
        Meter.CreateHistogram<double>(
            name: "audit.write.duration",
            unit: "ms",
            description: "Latency of an IAuditWriter.WriteAsync call.");

    /// <summary>Number of fail-closed write failures.</summary>
    public static readonly Counter<long> WriteFailures =
        Meter.CreateCounter<long>(
            name: "audit.write.failures",
            unit: "{event}",
            description: "Audit writes that failed (fail-closed paths bubble up to the caller).");

    /// <summary>Best-effort path drop count.</summary>
    public static readonly Counter<long> DroppedCount =
        Meter.CreateCounter<long>(
            name: "audit.dropped_count",
            unit: "{event}",
            description: "Best-effort audit events dropped (e.g. read paths under pressure).");

    /// <summary>Duplicate-suppression drop count (idempotent ingestion).</summary>
    public static readonly Counter<long> DuplicateDroppedCount =
        Meter.CreateCounter<long>(
            name: "audit.duplicate_dropped_count",
            unit: "{event}",
            description: "Audit writes dropped because the CloudEvents id was already ingested.");

    // -------- Interceptor (W2-A) --------

    /// <summary>RPC methods skipped via <c>[NoAudit("reason")]</c>.</summary>
    public static readonly Counter<long> SkippedCount =
        Meter.CreateCounter<long>(
            name: "audit.skipped_count",
            unit: "{event}",
            description: "RPC invocations that were not audited due to [NoAudit]; tagged with reason.");

    // -------- Outbox / Change Feed Processor (W2-B) --------
    //
    // Names are aligned with the W2-B task contract:
    //   audit.outbox.lag_seconds, audit.outbox.depth, audit.outbox.processed_total,
    //   audit.outbox.errors_total, audit.outbox.shipping_duration_ms.
    //
    // The depth gauge is observable: AuditOutboxWorker updates the latest observed pending
    // count via SetOutboxDepth(...). This is an approximation per worker instance; an
    // authoritative count requires the Cosmos ChangeFeedEstimator (W3 follow-up).

    /// <summary>Cosmos→Append Blob outbox replication lag, seconds.</summary>
    public static readonly Histogram<double> OutboxLagSeconds =
        Meter.CreateHistogram<double>(
            name: "audit.outbox.lag_seconds",
            unit: "s",
            description: "Estimated lag between an outbox document's Cosmos _ts and the time it was processed by the worker.");

    /// <summary>Outbox shipping (Cosmos→Blob) duration, milliseconds.</summary>
    public static readonly Histogram<double> OutboxShippingDurationMs =
        Meter.CreateHistogram<double>(
            name: "audit.outbox.shipping_duration_ms",
            unit: "ms",
            description: "Duration of a single outbox document shipping attempt (pending→shipping→shipped).");

    /// <summary>Successfully shipped outbox documents.</summary>
    public static readonly Counter<long> OutboxProcessedTotal =
        Meter.CreateCounter<long>(
            name: "audit.outbox.processed_total",
            unit: "{event}",
            description: "Outbox documents that completed shipping (status reached 'shipped').");

    /// <summary>Outbox processing errors (per-document or per-batch).</summary>
    public static readonly Counter<long> OutboxErrorsTotal =
        Meter.CreateCounter<long>(
            name: "audit.outbox.errors_total",
            unit: "{error}",
            description: "Outbox processing errors. Includes Change Feed Processor error notifications and per-document shipping failures.");

    private static long s_outboxDepth;

    /// <summary>
    /// Observable gauge reporting the latest depth (pending document count) observed by the
    /// worker. Updated via <see cref="SetOutboxDepth(long)"/>.
    /// </summary>
    public static readonly ObservableGauge<long> OutboxDepth =
        Meter.CreateObservableGauge<long>(
            name: "audit.outbox.depth",
            observeValue: () => Interlocked.Read(ref s_outboxDepth),
            unit: "{event}",
            description: "Latest pending-document count observed by AuditOutboxWorker. Approximate; per-instance.");

    /// <summary>Update the observable depth gauge with the latest pending count.</summary>
    public static void SetOutboxDepth(long value) => Interlocked.Exchange(ref s_outboxDepth, value);
}
