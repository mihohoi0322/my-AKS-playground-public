using System.Diagnostics.Metrics;
using HRSystem.Shared.Audit;

namespace HRSystem.Shared.Tests.Audit.Outbox;

/// <summary>
/// Asserts that the OTel instruments required by docs/features/audit-log.md (W2-B contract)
/// are registered with the audit Meter under the expected names. These names appear on
/// Prometheus dashboards / Application Insights metric explorers; renaming them silently
/// would break operations.
/// </summary>
public sealed class AuditOutboxMetricsTests
{
    private static readonly string[] RequiredInstruments =
    [
        "audit.outbox.lag_seconds",
        "audit.outbox.shipping_duration_ms",
        "audit.outbox.processed_total",
        "audit.outbox.errors_total",
        "audit.outbox.depth",
    ];

    [Fact]
    public void AllRequiredOutboxInstruments_AreRegistered()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AuditMetrics.SourceName)
                {
                    seen.Add(instrument.Name);
                }
            },
        };
        listener.Start();

        // Touch the static class so the field initialisers run and instruments are published.
        _ = AuditMetrics.OutboxLagSeconds;
        _ = AuditMetrics.OutboxShippingDurationMs;
        _ = AuditMetrics.OutboxProcessedTotal;
        _ = AuditMetrics.OutboxErrorsTotal;
        _ = AuditMetrics.OutboxDepth;

        foreach (var name in RequiredInstruments)
        {
            Assert.Contains(name, seen);
        }
    }

    [Fact]
    public void OutboxDepth_IsObservable_AndReflectsLatestSetValue()
    {
        AuditMetrics.SetOutboxDepth(42);
        long observed = -1;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AuditMetrics.SourceName
                    && instrument.Name == "audit.outbox.depth")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            if (instrument.Name == "audit.outbox.depth") observed = value;
        });
        listener.Start();
        listener.RecordObservableInstruments();

        Assert.Equal(42, observed);
    }

    [Fact]
    public void OutboxCounters_CanBeIncremented_WithoutThrowing()
    {
        // Smoke-test: AuditOutboxWorker increments these from multiple phases. Just confirm
        // the static instruments are usable from test code (no NRE / not-yet-initialised).
        AuditMetrics.OutboxProcessedTotal.Add(1);
        AuditMetrics.OutboxErrorsTotal.Add(1, new KeyValuePair<string, object?>("phase", "test"));
        AuditMetrics.OutboxLagSeconds.Record(0.5);
        AuditMetrics.OutboxShippingDurationMs.Record(12.0);
    }
}
