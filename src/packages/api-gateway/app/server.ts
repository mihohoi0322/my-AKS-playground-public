import Fastify, { type FastifyInstance } from "fastify";
import cors from "@fastify/cors";
import { randomUUID } from "node:crypto";
import { trace } from "@opentelemetry/api";
import { recordSpanError } from "./telemetry.js";
import { loadConfigFromEnv, type AppConfig } from "./config.js";
import { RedisClient } from "./redis-client.js";
import {
  createGrpcClients,
  type GrpcClients,
} from "./grpc-client.js";
import { registerRootRoute } from "./routes/root.js";
import { registerHealthRoute } from "./routes/health.js";
import { registerEmployeeRoutes } from "./routes/employees.js";
import { registerAttendanceRoutes } from "./routes/attendance.js";
import { registerOrganizationRoutes } from "./routes/organizations.js";
import { registerRealtimeStreamRoute } from "./routes/realtime/stream.js";
import { SessionStore } from "./lib/session/sessionStore.js";
import { ConnectionLimiter } from "./lib/realtime/connectionLimiter.js";

export async function buildServer(): Promise<FastifyInstance> {
  const config = loadConfigFromEnv();

  const app = Fastify({
    logger: {
      level: config.LOG_LEVEL,
    },
  });

  // Store config and redis client in app context
  app.decorate("config", config);
  app.decorate("redisClient", null as RedisClient | null);
  app.decorate(
    "grpcClients",
    createGrpcClients(
      config.EMPLOYEE_SERVICE_URL,
      config.ATTENDANCE_SERVICE_URL,
      config.ORGANIZATION_SERVICE_URL,
    ),
  );

  // CORS — allow web-ui origin
  await app.register(cors, {
    origin: true,
    methods: ["GET", "POST", "PATCH", "PUT", "DELETE", "OPTIONS"],
  });

  // X-Request-ID middleware
  app.addHook("onRequest", async (request, reply) => {
    const requestId =
      (request.headers["x-request-id"] as string) || randomUUID();
    request.requestId = requestId;
    reply.header("X-Request-ID", requestId);

    // Attach to current span
    try {
      const span = trace.getActiveSpan();
      if (span?.isRecording()) {
        span.setAttribute("http.request_id", requestId);
      }
    } catch {
      // best-effort
    }
  });

  // Global error handler
  app.setErrorHandler(async (error, request, reply) => {
    request.log.error(error, "Unhandled exception");
    recordSpanError(error instanceof Error ? error : new Error(String(error)));

    const errorResponse = {
      error: "Internal Server Error",
      detail:
        config.LOG_LEVEL === "debug"
          ? error instanceof Error
            ? error.message
            : String(error)
          : undefined,
      timestamp: new Date().toISOString(),
      request_id: request.requestId,
    };

    return reply.status(500).send(errorResponse);
  });

  // Register routes
  registerRootRoute(app);
  registerHealthRoute(app);
  registerEmployeeRoutes(app);
  registerAttendanceRoutes(app);
  registerOrganizationRoutes(app);
  // Lifecycle: connect Redis on startup
  app.addHook("onReady", async () => {
    if (config.REDIS_ENABLED && config.REDIS_HOST) {
      app.log.info(
        `Setting up Redis client for ${config.REDIS_HOST}:${config.REDIS_PORT}`,
      );
      const redisClient = new RedisClient(
        config.REDIS_HOST,
        config.REDIS_PORT,
        config,
      );
      try {
        await redisClient.connect();
        app.redisClient = redisClient;
        app.log.info("Successfully connected to Redis at startup");

        // SSE wiring requires Redis. Register the route only when both flags align.
        if (config.SSE_ENABLED) {
          const shared = redisClient.getRawClient();
          if (!shared) {
            app.log.warn("SSE_ENABLED but Redis client unavailable; skipping SSE route");
          } else if (!config.ACTOR_HASH_KEY) {
            app.log.warn("SSE_ENABLED but ACTOR_HASH_KEY missing; skipping SSE route");
          } else {
            const blocking = shared.duplicate();
            const sessionStore = new SessionStore(shared, {
              ttlSec: config.SESSION_TTL_SEC,
            });
            const limiter = new ConnectionLimiter(shared, config.POD_NAME, {
              actorMax: config.SSE_ACTOR_MAX,
              podMax: config.SSE_POD_MAX,
            });
            const allowedOrigins = config.ALLOWED_ORIGINS
              ? config.ALLOWED_ORIGINS.split(",").map((s) => s.trim()).filter(Boolean)
              : [];
            registerRealtimeStreamRoute(app, {
              sessionStore,
              limiter,
              blockingRedis: blocking,
              sharedRedis: shared,
              allowedOrigins,
              actorHashKey: config.ACTOR_HASH_KEY,
              heartbeatIntervalMs: config.SSE_HEARTBEAT_INTERVAL_MS,
              sessionCheckIntervalMs: config.SSE_SESSION_CHECK_INTERVAL_MS,
              replayWindowSec: config.SSE_REPLAY_WINDOW_SEC,
            });
            app.decorate("sseBlockingRedis", blocking);
            app.log.info("SSE route /api/realtime/v1/stream registered");
          }
        }
      } catch (err) {
        app.log.warn(`Failed to connect to Redis at startup: ${err}`);
      }
    }
  });

  // Lifecycle: graceful shutdown
  app.addHook("onClose", async () => {
    app.log.info("Shutting down AKS HR System Lab");
    // Wait for in-flight requests
    await new Promise((resolve) => setTimeout(resolve, 5000));
    if (app.sseBlockingRedis) {
      try {
        await app.sseBlockingRedis.quit();
      } catch {
        /* best-effort */
      }
    }
    if (app.redisClient) {
      app.log.info("Closing Redis connection");
      await app.redisClient.close();
    }
    app.log.info("Application shutdown complete");
  });

  return app;
}

// Fastify type augmentation
declare module "fastify" {
  interface FastifyInstance {
    config: AppConfig;
    redisClient: RedisClient | null;
    grpcClients: GrpcClients;
    sseBlockingRedis?: { quit: () => Promise<unknown> } | null;
  }
  interface FastifyRequest {
    requestId: string;
  }
}
