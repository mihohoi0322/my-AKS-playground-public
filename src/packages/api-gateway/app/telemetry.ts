import { NodeSDK } from "@opentelemetry/sdk-node";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-proto";
import { OTLPMetricExporter } from "@opentelemetry/exporter-metrics-otlp-proto";
import { PeriodicExportingMetricReader } from "@opentelemetry/sdk-metrics";
import {
  AggregationTemporality,
} from "@opentelemetry/sdk-metrics";
import { FastifyInstrumentation } from "@opentelemetry/instrumentation-fastify";
import { IORedisInstrumentation } from "@opentelemetry/instrumentation-ioredis";
import { HttpInstrumentation } from "@opentelemetry/instrumentation-http";
import { TraceIdRatioBasedSampler } from "@opentelemetry/sdk-trace-node";
import { resourceFromAttributes } from "@opentelemetry/resources";
import {
  ATTR_SERVICE_NAME,
  ATTR_SERVICE_VERSION,
} from "@opentelemetry/semantic-conventions";
import { metrics, trace } from "@opentelemetry/api";
import type { Meter, Tracer } from "@opentelemetry/api";
import { loadConfigFromEnv } from "./config.js";

let _sdk: NodeSDK | null = null;
let _meter: Meter | null = null;
let _tracer: Tracer | null = null;
let _initialized = false;

/**
 * Configure vendor-neutral OpenTelemetry with OTLP exporter.
 * Must be called before importing Fastify or ioredis.
 */
export function setupTelemetry(): void {
  if (_initialized) return;
  _initialized = true;

  const config = loadConfigFromEnv();

  if (!config.TELEMETRY_ENABLED) return;

  const hasEndpoint =
    process.env.OTEL_EXPORTER_OTLP_ENDPOINT ||
    process.env.OTEL_EXPORTER_OTLP_TRACES_ENDPOINT;

  if (!hasEndpoint) return;

  const resource = resourceFromAttributes({
    [ATTR_SERVICE_NAME]: "chaos-app",
    [ATTR_SERVICE_VERSION]: "0.1.0",
  });

  // Delta temporality required for Application Insights OTLP
  const metricReader = new PeriodicExportingMetricReader({
    exporter: new OTLPMetricExporter({
      temporalityPreference: AggregationTemporality.DELTA,
    }),
  });

  // Sampling: respect OTEL_TRACES_SAMPLER env or fall back to config
  const sampler =
    !process.env.OTEL_TRACES_SAMPLER && config.TELEMETRY_SAMPLING_RATE < 1.0
      ? new TraceIdRatioBasedSampler(config.TELEMETRY_SAMPLING_RATE)
      : undefined;

  _sdk = new NodeSDK({
    resource,
    traceExporter: new OTLPTraceExporter(),
    metricReader,
    sampler,
    instrumentations: [
      new HttpInstrumentation(),
      new FastifyInstrumentation(),
      new IORedisInstrumentation(),
    ],
  });

  _sdk.start();

  _meter = metrics.getMeter("aks-hrsystem-lab", "0.1.0");
  _tracer = trace.getTracer("aks-hrsystem-lab", "0.1.0");
}

export function getMeter(): Meter | null {
  return _meter;
}

export function getTracer(): Tracer | null {
  return _tracer;
}

/** Record exception on current span. */
export function recordSpanError(exc: Error): void {
  try {
    const span = trace.getActiveSpan();
    if (span?.isRecording()) {
      span.setStatus({ code: 2, message: exc.message }); // StatusCode.ERROR = 2
      span.recordException(exc);
    }
  } catch {
    // best-effort
  }
}

/** Record Redis connection metrics if custom metrics enabled. */
export function recordRedisMetrics(
  connected: boolean,
  latencyMs: number,
): void {
  const config = loadConfigFromEnv();
  if (!config.CUSTOM_METRICS_ENABLED || !_meter) return;

  try {
    const connGauge = _meter.createGauge("redis_connection_status", {
      description:
        "Redis connection status (1=connected, 0=disconnected)",
    });
    connGauge.record(connected ? 1 : 0);

    if (connected && latencyMs >= 0) {
      const latHist = _meter.createHistogram(
        "redis_connection_latency_ms",
        {
          description: "Redis connection latency (ms)",
          unit: "ms",
        },
      );
      latHist.record(latencyMs);
    }
  } catch {
    // best-effort
  }
}

/** Reset telemetry state (for testing). */
export function resetTelemetry(): void {
  _initialized = false;
  _meter = null;
  _tracer = null;
  if (_sdk) {
    _sdk.shutdown().catch(() => {});
    _sdk = null;
  }
}
