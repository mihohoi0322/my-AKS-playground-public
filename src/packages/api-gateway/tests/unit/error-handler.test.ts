import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import Fastify from "fastify";
import type { FastifyRequest, FastifyReply, FastifyError } from "fastify";
import { registerRootRoute } from "../../app/routes/root.js";
import { registerHealthRoute } from "../../app/routes/health.js";
import { randomUUID } from "node:crypto";
import { trace } from "@opentelemetry/api";
import { recordSpanError } from "../../app/telemetry.js";
import type { AppConfig } from "../../app/config.js";

vi.mock("../../app/telemetry.js", () => ({
  recordRedisMetrics: vi.fn(),
  recordSpanError: vi.fn(),
  setupTelemetry: vi.fn(),
}));

function buildTestConfig(overrides: Partial<AppConfig> = {}): AppConfig {
  return {
    APP_PORT: 8000,
    LOG_LEVEL: "debug",
    REDIS_ENABLED: false,
    REDIS_SSL: true,
    REDIS_HOST: undefined,
    REDIS_PORT: 10000,
    REDIS_MAX_CONNECTIONS: 50,
    REDIS_SOCKET_TIMEOUT: 3000,
    REDIS_SOCKET_CONNECT_TIMEOUT: 3000,
    REDIS_MAX_RETRIES: 1,
    AZURE_CLIENT_ID: undefined,
    TELEMETRY_ENABLED: false,
    CUSTOM_METRICS_ENABLED: false,
    TELEMETRY_SAMPLING_RATE: 0.1,
    EMPLOYEE_SERVICE_URL: "localhost:50051",
    ATTENDANCE_SERVICE_URL: "localhost:50052",
    ORGANIZATION_SERVICE_URL: "localhost:50053",
    SSE_ENABLED: false,
    SSE_ACTOR_MAX: 3,
    SSE_POD_MAX: 500,
    SSE_HEARTBEAT_INTERVAL_MS: 15000,
    SSE_SESSION_CHECK_INTERVAL_MS: 20000,
    SSE_REPLAY_WINDOW_SEC: 60,
    SESSION_TTL_SEC: 3600,
    ALLOWED_ORIGINS: "",
    ACTOR_HASH_KEY: "",
    POD_NAME: "local",
    ...overrides,
  };
}

describe("Error handler", () => {
  let app: ReturnType<typeof Fastify>;

  beforeEach(async () => {
    app = Fastify({ logger: false });
    app.decorate("config", buildTestConfig());
    app.decorate("redisClient", null);
    app.decorate("requestId", "");
    app.addHook("onRequest", async (request: FastifyRequest) => {
      request.requestId = "test-request-id";
    });

    // Add X-Request-ID middleware
    app.addHook("onRequest", async (request: FastifyRequest, reply: FastifyReply) => {
      const requestId =
        (request.headers["x-request-id"] as string) || randomUUID();
      request.requestId = requestId;
      reply.header("X-Request-ID", requestId);
      try {
        const span = trace.getActiveSpan();
        if (span?.isRecording()) {
          span.setAttribute("http.request_id", requestId);
        }
      } catch {
        // best-effort
      }
    });

    // Set error handler
    app.setErrorHandler(async (error: FastifyError, request: FastifyRequest, reply: FastifyReply) => {
      recordSpanError(
        error instanceof Error ? error : new Error(String(error)),
      );
      return reply.status(500).send({
        error: "Internal Server Error",
        detail: error instanceof Error ? error.message : String(error),
        timestamp: new Date().toISOString(),
        request_id: request.requestId,
      });
    });

    registerRootRoute(app);
    registerHealthRoute(app);

    // Add a test route that throws
    app.get("/throw", async () => {
      throw new Error("Test error");
    });

    await app.ready();
  });

  afterEach(async () => {
    await app.close();
  });

  it("should return 500 with error details for unhandled exceptions", async () => {
    const response = await app.inject({ method: "GET", url: "/throw" });
    expect(response.statusCode).toBe(500);
    const body = response.json();
    expect(body.error).toBe("Internal Server Error");
    expect(body.detail).toBe("Test error");
    expect(body.request_id).toBeDefined();
  });

  it("should propagate X-Request-ID header", async () => {
    const response = await app.inject({
      method: "GET",
      url: "/",
      headers: { "x-request-id": "custom-id-123" },
    });
    expect(response.headers["x-request-id"]).toBe("custom-id-123");
  });

  it("should generate X-Request-ID when not provided", async () => {
    const response = await app.inject({ method: "GET", url: "/" });
    expect(response.headers["x-request-id"]).toBeDefined();
    expect(typeof response.headers["x-request-id"]).toBe("string");
  });
});
