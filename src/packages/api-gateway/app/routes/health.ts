import type { FastifyInstance } from "fastify";
import { HealthResponseSchema } from "../schemas.js";
import { recordRedisMetrics } from "../telemetry.js";
import type { HealthResponse } from "../schemas.js";

// Health check cache (5-second TTL to reduce Redis load)
let healthCache: {
  payload: HealthResponse;
  statusCode: number;
  ts: number;
} | null = null;
const HEALTH_CACHE_TTL = 5000; // ms

function isHealthCacheValid(): boolean {
  if (!healthCache) return false;
  return performance.now() - healthCache.ts < HEALTH_CACHE_TTL;
}

export function registerHealthRoute(app: FastifyInstance): void {
  app.get(
    "/health",
    {
      schema: {
        response: {
          200: HealthResponseSchema,
          503: HealthResponseSchema,
        },
      },
    },
    async (_request, reply) => {
      // Return cached response if valid
      if (isHealthCacheValid() && healthCache) {
        if (healthCache.statusCode !== 200) {
          return reply
            .status(healthCache.statusCode as 503)
            .send(healthCache.payload);
        }
        return healthCache.payload;
      }

      const config = app.config;
      const client = app.redisClient;

      // If Redis is disabled, skip connection and treat as healthy
      if (!config.REDIS_ENABLED || !config.REDIS_HOST) {
        const resp: HealthResponse = {
          status: "healthy",
          redis: { connected: false, latency_ms: 0 },
          timestamp: new Date().toISOString(),
        };
        try {
          recordRedisMetrics(false, 0);
        } catch {
          // best-effort
        }
        healthCache = { payload: resp, statusCode: 200, ts: performance.now() };
        return resp;
      }

      let redisConnected = false;
      let redisLatencyMs = 0;

      if (client && config.REDIS_ENABLED) {
        try {
          const start = performance.now();
          await client.ping();
          redisLatencyMs = Math.round(performance.now() - start);
          redisConnected = true;
        } catch {
          redisConnected = false;
        }
      }

      const status = redisConnected ? "healthy" : "unhealthy";
      const resp: HealthResponse = {
        status,
        redis: { connected: redisConnected, latency_ms: redisLatencyMs },
        timestamp: new Date().toISOString(),
      };
      const code = status === "healthy" ? 200 : 503;

      try {
        recordRedisMetrics(redisConnected, redisLatencyMs);
      } catch {
        // best-effort
      }

      healthCache = { payload: resp, statusCode: code, ts: performance.now() };

      if (code !== 200) {
        return reply.status(code).send(resp);
      }
      return resp;
    },
  );
}

/** Reset health cache (for testing). */
export function resetHealthCache(): void {
  healthCache = null;
}
