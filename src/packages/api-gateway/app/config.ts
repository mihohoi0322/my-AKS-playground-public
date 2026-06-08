import { Type, type Static } from "@sinclair/typebox";

export const envSchema = Type.Object({
  APP_PORT: Type.Number({ default: 8000 }),
  LOG_LEVEL: Type.String({ default: "info" }),

  // Redis
  REDIS_ENABLED: Type.Boolean({ default: false }),
  REDIS_SSL: Type.Boolean({ default: true }),
  REDIS_HOST: Type.Optional(Type.String()),
  REDIS_PORT: Type.Number({ default: 10000 }),
  REDIS_MAX_CONNECTIONS: Type.Number({ default: 50 }),
  REDIS_SOCKET_TIMEOUT: Type.Number({ default: 3000 }),
  REDIS_SOCKET_CONNECT_TIMEOUT: Type.Number({ default: 3000 }),
  REDIS_MAX_RETRIES: Type.Number({ default: 1 }),

  // Entra ID (Workload Identity / UAMI)
  AZURE_CLIENT_ID: Type.Optional(Type.String()),

  // Telemetry
  TELEMETRY_ENABLED: Type.Boolean({ default: true }),
  CUSTOM_METRICS_ENABLED: Type.Boolean({ default: true }),
  TELEMETRY_SAMPLING_RATE: Type.Number({ default: 0.1 }),

  // gRPC backend service URLs
  EMPLOYEE_SERVICE_URL: Type.String({
    default: "localhost:50051",
  }),
  ATTENDANCE_SERVICE_URL: Type.String({
    default: "localhost:50052",
  }),
  ORGANIZATION_SERVICE_URL: Type.String({
    default: "localhost:50053",
  }),

  // SSE / realtime
  SSE_ENABLED: Type.Boolean({ default: false }),
  SSE_ACTOR_MAX: Type.Number({ default: 3 }),
  SSE_POD_MAX: Type.Number({ default: 500 }),
  SSE_HEARTBEAT_INTERVAL_MS: Type.Number({ default: 15_000 }),
  SSE_SESSION_CHECK_INTERVAL_MS: Type.Number({ default: 20_000 }),
  SSE_REPLAY_WINDOW_SEC: Type.Number({ default: 60 }),
  SESSION_TTL_SEC: Type.Number({ default: 3600 }),
  /** Comma-separated list, exact match. Empty = no Origin enforcement (dev only). */
  ALLOWED_ORIGINS: Type.String({ default: "" }),
  /** HMAC key for tenant-scoped actorHash. Required when SSE enabled. */
  ACTOR_HASH_KEY: Type.String({ default: "" }),
  /** Pod name from Downward API; falls back to HOSTNAME / "local". */
  POD_NAME: Type.String({ default: "local" }),
});

export type AppConfig = Static<typeof envSchema>;

/**
 * Extract "host:port" from a URL string or return as-is.
 * Aspire passes env vars like "http://localhost:5280"; gRPC needs "localhost:5280".
 *
 * NOTE: Only http(s) URLs are parsed. "host:port" forms (e.g.
 * "employee-service:50051") would otherwise be misinterpreted by `new URL()`
 * as a scheme ("employee-service:") and yield empty hostname/port.
 */
export function toGrpcTarget(raw: string): string {
  if (/^https?:\/\//i.test(raw)) {
    try {
      const u = new URL(raw);
      const port = u.port || (u.protocol === "https:" ? "443" : "80");
      return `${u.hostname}:${port}`;
    } catch {
      return raw;
    }
  }
  return raw;
}

/**
 * Load configuration from environment variables with defaults.
 * Used outside Fastify context (e.g., telemetry init).
 */
export function loadConfigFromEnv(): AppConfig {
  return {
    APP_PORT: parseInt(process.env.APP_PORT ?? "8000", 10),
    LOG_LEVEL: process.env.LOG_LEVEL ?? "info",
    REDIS_ENABLED: process.env.REDIS_ENABLED === "true",
    REDIS_SSL: process.env.REDIS_SSL !== "false",
    REDIS_HOST: process.env.REDIS_HOST ?? process.env.AZURE_REDIS_HOST,
    REDIS_PORT: parseInt(
      process.env.REDIS_PORT ?? process.env.AZURE_REDIS_PORT ?? "10000",
      10,
    ),
    REDIS_MAX_CONNECTIONS: parseInt(
      process.env.REDIS_MAX_CONNECTIONS ?? "50",
      10,
    ),
    REDIS_SOCKET_TIMEOUT: parseInt(
      process.env.REDIS_SOCKET_TIMEOUT ?? "3000",
      10,
    ),
    REDIS_SOCKET_CONNECT_TIMEOUT: parseInt(
      process.env.REDIS_SOCKET_CONNECT_TIMEOUT ?? "3000",
      10,
    ),
    REDIS_MAX_RETRIES: parseInt(process.env.REDIS_MAX_RETRIES ?? "1", 10),
    AZURE_CLIENT_ID: process.env.AZURE_CLIENT_ID,
    TELEMETRY_ENABLED: process.env.TELEMETRY_ENABLED !== "false",
    CUSTOM_METRICS_ENABLED: process.env.CUSTOM_METRICS_ENABLED !== "false",
    TELEMETRY_SAMPLING_RATE: parseFloat(
      process.env.TELEMETRY_SAMPLING_RATE ?? "0.1",
    ),
    EMPLOYEE_SERVICE_URL: toGrpcTarget(
      process.env.EMPLOYEE_SERVICE_URL ?? "localhost:50051",
    ),
    ATTENDANCE_SERVICE_URL: toGrpcTarget(
      process.env.ATTENDANCE_SERVICE_URL ?? "localhost:50052",
    ),
    ORGANIZATION_SERVICE_URL: toGrpcTarget(
      process.env.ORGANIZATION_SERVICE_URL ?? "localhost:50053",
    ),
    SSE_ENABLED: process.env.SSE_ENABLED === "true",
    SSE_ACTOR_MAX: parseInt(process.env.SSE_ACTOR_MAX ?? "3", 10),
    SSE_POD_MAX: parseInt(process.env.SSE_POD_MAX ?? "500", 10),
    SSE_HEARTBEAT_INTERVAL_MS: parseInt(
      process.env.SSE_HEARTBEAT_INTERVAL_MS ?? "15000",
      10,
    ),
    SSE_SESSION_CHECK_INTERVAL_MS: parseInt(
      process.env.SSE_SESSION_CHECK_INTERVAL_MS ?? "20000",
      10,
    ),
    SSE_REPLAY_WINDOW_SEC: parseInt(
      process.env.SSE_REPLAY_WINDOW_SEC ?? "60",
      10,
    ),
    SESSION_TTL_SEC: parseInt(process.env.SESSION_TTL_SEC ?? "3600", 10),
    ALLOWED_ORIGINS: process.env.ALLOWED_ORIGINS ?? "",
    ACTOR_HASH_KEY: process.env.ACTOR_HASH_KEY ?? "",
    POD_NAME: process.env.POD_NAME ?? process.env.HOSTNAME ?? "local",
  };
}
