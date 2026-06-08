import type { FastifyInstance } from "fastify";
import { MainResponseSchema, ErrorResponseSchema } from "../schemas.js";
import { recordRedisMetrics } from "../telemetry.js";

let requestCounter = 0;

export function registerRootRoute(app: FastifyInstance): void {
  app.get(
    "/",
    {
      schema: {
        response: {
          200: MainResponseSchema,
          503: ErrorResponseSchema,
        },
      },
    },
    async (request, reply) => {
      const timestamp = new Date().toISOString();
      let redisData: string | null = "Redis unavailable";
      let redisError: string | null = null;

      const client = app.redisClient;
      const config = app.config;

      if (client && config.REDIS_ENABLED) {
        try {
          const key = "chaos_lab:data:sample";
          let val = await client.get(key);
          if (!val) {
            val = `Data created at ${timestamp}`;
            await client.set(key, val);
          }
          redisData = val;

          // Increment every 10th request deterministically
          requestCounter++;
          if (requestCounter % 10 === 0) {
            await client.increment("chaos_lab:counter:requests");
          }
        } catch (err) {
          request.log.error(`Redis operation failed: ${err}`);
          redisError = String(err);
        }
      }

      // Emit custom metrics (best-effort)
      try {
        const connected =
          client !== null && redisError === null && config.REDIS_ENABLED;
        recordRedisMetrics(connected, 0);
      } catch {
        // best-effort
      }

      if (config.REDIS_ENABLED && redisError) {
        return reply.status(503).send({
          error: "Service Unavailable",
          detail: `Redis operation failed: ${redisError}`,
          timestamp,
          request_id: request.requestId,
        });
      }

      return {
        message: "Hello from AKS HR System Lab",
        redis_data: redisData,
        timestamp,
      };
    },
  );
}
