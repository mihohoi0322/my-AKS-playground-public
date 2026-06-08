import { describe, it, expect, vi, beforeEach } from "vitest";

// Must mock before importing
vi.mock("@opentelemetry/sdk-node", () => ({
  NodeSDK: vi.fn().mockImplementation(() => ({
    start: vi.fn(),
    shutdown: vi.fn().mockResolvedValue(undefined),
  })),
}));
vi.mock("@opentelemetry/exporter-trace-otlp-proto", () => ({
  OTLPTraceExporter: vi.fn(),
}));
vi.mock("@opentelemetry/exporter-metrics-otlp-proto", () => ({
  OTLPMetricExporter: vi.fn(),
}));
vi.mock("@opentelemetry/sdk-metrics", async () => {
  const actual = await vi.importActual("@opentelemetry/sdk-metrics");
  return {
    ...actual,
    PeriodicExportingMetricReader: vi.fn(),
  };
});
vi.mock("@opentelemetry/instrumentation-fastify", () => ({
  FastifyInstrumentation: vi.fn(),
}));
vi.mock("@opentelemetry/instrumentation-ioredis", () => ({
  IORedisInstrumentation: vi.fn(),
}));
vi.mock("@opentelemetry/instrumentation-http", () => ({
  HttpInstrumentation: vi.fn(),
}));

describe("telemetry", () => {
  beforeEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it("should not initialize when TELEMETRY_ENABLED is false", async () => {
    vi.stubEnv("TELEMETRY_ENABLED", "false");
    const { setupTelemetry, getMeter } = await import(
      "../../app/telemetry.js"
    );
    setupTelemetry();
    expect(getMeter()).toBeNull();
  });

  it("should not initialize when no OTLP endpoint", async () => {
    vi.stubEnv("TELEMETRY_ENABLED", "true");
    // Ensure no OTLP endpoint
    delete process.env.OTEL_EXPORTER_OTLP_ENDPOINT;
    delete process.env.OTEL_EXPORTER_OTLP_TRACES_ENDPOINT;
    const { setupTelemetry, getMeter } = await import(
      "../../app/telemetry.js"
    );
    setupTelemetry();
    expect(getMeter()).toBeNull();
  });

  it("should prevent duplicate initialization", async () => {
    vi.stubEnv("TELEMETRY_ENABLED", "false");
    const { setupTelemetry } = await import("../../app/telemetry.js");
    setupTelemetry();
    setupTelemetry(); // second call should be no-op
    // No error thrown = success
  });

  it("recordRedisMetrics should not throw when meter is null", async () => {
    vi.stubEnv("TELEMETRY_ENABLED", "false");
    const { recordRedisMetrics } = await import("../../app/telemetry.js");
    expect(() => recordRedisMetrics(true, 10)).not.toThrow();
  });

  it("recordSpanError should not throw when no active span", async () => {
    const { recordSpanError } = await import("../../app/telemetry.js");
    expect(() => recordSpanError(new Error("test"))).not.toThrow();
  });
});
