import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import Fastify from "fastify";
import type { FastifyRequest } from "fastify";
import {
  registerHealthRoute,
  resetHealthCache,
} from "../../app/routes/health.js";
import type { AppConfig } from "../../app/config.js";

// Mock telemetry
vi.mock("../../app/telemetry.js", () => ({
  recordRedisMetrics: vi.fn(),
  recordSpanError: vi.fn(),
  setupTelemetry: vi.fn(),
}));

function buildTestConfig(overrides: Partial<AppConfig> = {}): AppConfig {
  return {
    APP_PORT: 8000,
    LOG_LEVEL: "info",
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

describe("GET /health", () => {
  let app: ReturnType<typeof Fastify>;

  beforeEach(async () => {
    resetHealthCache();
    app = Fastify({ logger: false });
    app.decorate("config", buildTestConfig());
    app.decorate("redisClient", null);
    app.decorate("requestId", "");
    app.addHook("onRequest", async (request: FastifyRequest) => {
      request.requestId = "test-request-id";
    });
    registerHealthRoute(app);
    await app.ready();
  });

  afterEach(async () => {
    await app.close();
  });

  it("should return healthy when Redis disabled", async () => {
    const response = await app.inject({ method: "GET", url: "/health" });
    expect(response.statusCode).toBe(200);
    const body = response.json();
    expect(body.status).toBe("healthy");
    expect(body.redis.connected).toBe(false);
  });

  it("should return healthy when Redis ping succeeds", async () => {
    const mockRedis = {
      ping: vi.fn().mockResolvedValue(true),
    };
    app.config = buildTestConfig({
      REDIS_ENABLED: true,
      REDIS_HOST: "localhost",
    });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    app.redisClient = mockRedis as any;

    const response = await app.inject({ method: "GET", url: "/health" });
    expect(response.statusCode).toBe(200);
    const body = response.json();
    expect(body.status).toBe("healthy");
    expect(body.redis.connected).toBe(true);
  });

  it("should return unhealthy when Redis ping fails", async () => {
    const mockRedis = {
      ping: vi.fn().mockRejectedValue(new Error("Connection refused")),
    };
    app.config = buildTestConfig({
      REDIS_ENABLED: true,
      REDIS_HOST: "localhost",
    });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    app.redisClient = mockRedis as any;

    const response = await app.inject({ method: "GET", url: "/health" });
    expect(response.statusCode).toBe(503);
    const body = response.json();
    expect(body.status).toBe("unhealthy");
    expect(body.redis.connected).toBe(false);
  });

  it("should return cached response within TTL", async () => {
    // First request
    const response1 = await app.inject({ method: "GET", url: "/health" });
    expect(response1.statusCode).toBe(200);

    // Second request should be cached
    const response2 = await app.inject({ method: "GET", url: "/health" });
    expect(response2.statusCode).toBe(200);
    expect(response2.json().status).toBe("healthy");
  });
});
