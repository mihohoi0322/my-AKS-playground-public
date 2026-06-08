using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HRSystem.Shared.Telemetry;

public static class TelemetryExtensions
{
    public static IServiceCollection AddHRSystemTelemetry(this IServiceCollection services, string serviceName)
    {
        // When running under Aspire, ServiceDefaults configures OTel with UseOtlpExporter() (cross-cutting).
        // Signal-specific AddOtlpExporter() cannot coexist with UseOtlpExporter() on the same IServiceCollection.
        // Detect Aspire mode via OTEL_EXPORTER_OTLP_ENDPOINT and skip per-signal exporters.
        var aspireManagesExport = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceNamespace: "hrsystem"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddGrpcClientInstrumentation()
                    .AddSource(serviceName);
                if (!aspireManagesExport) tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddMeter(serviceName);
                if (!aspireManagesExport) metrics.AddOtlpExporter();
            });

        return services;
    }
}
