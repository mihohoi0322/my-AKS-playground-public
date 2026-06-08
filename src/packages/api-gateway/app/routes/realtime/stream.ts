import type { FastifyInstance, FastifyReply, FastifyRequest } from "fastify";
import { randomUUID } from "node:crypto";
import { SessionStore } from "../../lib/session/sessionStore.js";
import { ConnectionLimiter } from "../../lib/realtime/connectionLimiter.js";
import {
  readStream,
  replayWindow,
  streamKey,
  type StreamMessage,
} from "../../lib/realtime/streamConsumer.js";
import { filterPayload } from "../../lib/realtime/payloadFilter.js";
import {
  incConnections,
  recordDroppedPayload,
  recordReplayHit,
  recordReplayMiss,
  recordSessionRevokedDuringStream,
} from "../../lib/realtime/metrics.js";

const SESSION_COOKIE_NAME = "__Host-hrsystem-session";
const ALLOWED_TOPICS = new Set(["dashboard", "notifications"]);

interface StreamQuery {
  topic?: string;
}

/** Parse a Cookie header into a flat key/value map. */
function parseCookies(header: string | undefined): Record<string, string> {
  if (!header) return {};
  const out: Record<string, string> = {};
  for (const part of header.split(";")) {
    const eq = part.indexOf("=");
    if (eq < 0) continue;
    const k = part.slice(0, eq).trim();
    const v = part.slice(eq + 1).trim();
    if (k) out[k] = v;
  }
  return out;
}

/** Strict allow-list check on Origin header. */
function isOriginAllowed(
  origin: string | undefined,
  allowed: readonly string[],
): boolean {
  if (!origin) return false;
  return allowed.includes(origin);
}

interface RouteDeps {
  sessionStore: SessionStore;
  limiter: ConnectionLimiter;
  /** Dedicated ioredis client for XREAD BLOCK. */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  blockingRedis: any;
  /** Shared ioredis client for XRANGE / lookups. */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  sharedRedis: any;
  allowedOrigins: readonly string[];
  actorHashKey: string;
  heartbeatIntervalMs?: number;
  sessionCheckIntervalMs?: number;
  replayWindowSec?: number;
}

const PING_FRAME = `: ping\n\n`;

function writeSse(
  reply: FastifyReply,
  msg: StreamMessage,
  data: Record<string, unknown>,
): boolean {
  const frame =
    `id: ${msg.id}\n` +
    `event: ${msg.eventType}\n` +
    `data: ${JSON.stringify(data)}\n\n`;
  return reply.raw.write(frame);
}

function writeAuthExpired(reply: FastifyReply): void {
  try {
    reply.raw.write(`event: auth-expired\ndata: {}\n\n`);
  } catch {
    /* socket may already be closed */
  }
}

function waitForDrain(reply: FastifyReply, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    if (!reply.raw.writableNeedDrain || signal.aborted) {
      resolve();
      return;
    }
    const onDrain = (): void => {
      reply.raw.off("drain", onDrain);
      resolve();
    };
    const onAbort = (): void => {
      reply.raw.off("drain", onDrain);
      resolve();
    };
    reply.raw.once("drain", onDrain);
    signal.addEventListener("abort", onAbort, { once: true });
  });
}

export function registerRealtimeStreamRoute(
  app: FastifyInstance,
  deps: RouteDeps,
): void {
  const heartbeatIntervalMs = deps.heartbeatIntervalMs ?? 15_000;
  const sessionCheckIntervalMs = deps.sessionCheckIntervalMs ?? 20_000;
  const replayWindowSec = deps.replayWindowSec ?? 60;

  // Track open connections so we can drain them on shutdown.
  const open = new Set<{ abort: AbortController; reply: FastifyReply }>();

  app.addHook("onClose", async () => {
    for (const conn of open) {
      try {
        conn.abort.abort();
        conn.reply.raw.end();
      } catch {
        /* best-effort */
      }
    }
    open.clear();
  });

  app.get<{ Querystring: StreamQuery }>(
    "/api/realtime/v1/stream",
    async (
      request: FastifyRequest<{ Querystring: StreamQuery }>,
      reply: FastifyReply,
    ) => {
      // 1. Topic validation.
      const topic = request.query.topic;
      if (!topic || !ALLOWED_TOPICS.has(topic)) {
        return reply
          .status(400)
          .send({ error: "Invalid or missing topic" });
      }

      // 2. Origin allow-list (skip when no origins configured = wildcard for
      //    same-origin tools like curl/k6 in dev only). In prod the env var
      //    must be set.
      const origin = request.headers.origin;
      if (deps.allowedOrigins.length > 0) {
        if (!isOriginAllowed(origin, deps.allowedOrigins)) {
          return reply.status(403).send({ error: "Origin not allowed" });
        }
      }

      // 3. Cookie session lookup.
      const cookies = parseCookies(request.headers.cookie);
      const rawCookie = cookies[SESSION_COOKIE_NAME];
      if (!rawCookie) {
        return reply.status(401).send({ error: "Missing session cookie" });
      }
      const session = await deps.sessionStore.lookup(rawCookie);
      if (!session) {
        return reply.status(401).send({ error: "Invalid session" });
      }

      // 4. Connection-limit check (atomic via Lua).
      const connectionId = randomUUID();
      const limit = await deps.limiter.acquire(session.oid, connectionId);
      if (!limit.ok) {
        reply.header("Retry-After", "30");
        return reply.status(429).send({
          error: "Connection limit reached",
          reason: limit.reason,
          current: limit.current,
          limit: limit.limit,
        });
      }

      // ---- From here on we own a slot; everything must release it. ----
      const abort = new AbortController();
      const tracked = { abort, reply };
      open.add(tracked);
      let released = false;

      const release = async (): Promise<void> => {
        if (released) return;
        released = true;
        open.delete(tracked);
        clearInterval(heartbeatTimer);
        clearInterval(sessionCheckTimer);
        try {
          incConnections(-1, { topic });
        } catch {
          /* metrics best-effort */
        }
        try {
          await deps.limiter.release(session.oid, connectionId);
        } catch {
          /* best-effort */
        }
      };

      request.raw.on("close", () => {
        abort.abort();
        void release();
      });

      // 5. SSE headers.
      reply.raw.writeHead(200, {
        "Content-Type": "text/event-stream",
        "Cache-Control": "no-cache, no-transform",
        Connection: "keep-alive",
        "X-Accel-Buffering": "no",
      });
      reply.hijack();
      incConnections(1, { topic });

      // 6. Replay via Last-Event-ID header.
      const key = streamKey(session.tenantId, topic);
      const lastEventId = request.headers["last-event-id"] as
        | string
        | undefined;
      let cursor: string | undefined;
      if (lastEventId) {
        try {
          const replayed = await replayWindow(
            deps.sharedRedis,
            key,
            lastEventId,
            replayWindowSec,
          );
          if (replayed.length > 0) {
            recordReplayHit({ topic });
            for (const msg of replayed) {
              const filtered = filterPayload(msg.payload, deps.actorHashKey);
              if (!filtered) {
                recordDroppedPayload({ topic });
                continue;
              }
              writeSse(reply, msg, filtered);
              cursor = msg.id;
            }
          } else {
            recordReplayMiss({ topic });
          }
        } catch (err) {
          request.log.warn({ err }, "replay failed");
        }
      }

      // 7. Heartbeat & session-existence checks.
      const heartbeatTimer = setInterval(() => {
        try {
          reply.raw.write(PING_FRAME);
          void deps.limiter.heartbeat(session.oid, connectionId);
          void deps.sessionStore.touch(rawCookie);
        } catch {
          abort.abort();
        }
      }, heartbeatIntervalMs);
      heartbeatTimer.unref?.();

      const sessionCheckTimer = setInterval(async () => {
        try {
          const still = await deps.sessionStore.lookup(rawCookie);
          if (!still) {
            recordSessionRevokedDuringStream({ topic });
            writeAuthExpired(reply);
            abort.abort();
            try {
              reply.raw.end();
            } catch {
              /* socket may be closed */
            }
            await release();
          }
        } catch (err) {
          request.log.warn({ err }, "session check failed");
        }
      }, sessionCheckIntervalMs);
      sessionCheckTimer.unref?.();

      // 8. Live stream loop.
      try {
        for await (const msg of readStream(deps.blockingRedis, key, abort.signal, {
          startId: cursor ?? "$",
        })) {
          if (abort.signal.aborted) break;
          const filtered = filterPayload(msg.payload, deps.actorHashKey);
          if (!filtered) {
            recordDroppedPayload({ topic });
            continue;
          }
          // Cross-tenant safety: stream key is tenant-scoped, but double-check.
          if (filtered.tenantId !== session.tenantId) {
            recordDroppedPayload({ topic });
            continue;
          }
          const ok = writeSse(reply, msg, filtered);
          if (!ok) await waitForDrain(reply, abort.signal);
        }
      } catch (err) {
        if (!abort.signal.aborted) {
          request.log.error({ err }, "SSE stream loop crashed");
        }
      } finally {
        await release();
        try {
          reply.raw.end();
        } catch {
          /* socket may be closed */
        }
      }
    },
  );
}
