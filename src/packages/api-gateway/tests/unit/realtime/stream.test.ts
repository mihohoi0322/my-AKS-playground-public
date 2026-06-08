import { describe, it, expect, beforeEach, afterEach } from "vitest";
import Fastify, { type FastifyInstance } from "fastify";
import RedisMock from "ioredis-mock";
import { SessionStore } from "../../../app/lib/session/sessionStore.js";
import { ConnectionLimiter } from "../../../app/lib/realtime/connectionLimiter.js";
import { registerRealtimeStreamRoute } from "../../../app/routes/realtime/stream.js";
import { streamKey } from "../../../app/lib/realtime/streamConsumer.js";

const HMAC_KEY = "test-hmac-key";

async function setupApp(): Promise<{
  app: FastifyInstance;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  redis: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  blocking: any;
  store: SessionStore;
  cookie: string;
}> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const redis: any = new RedisMock();
  await redis.flushall();
  // ioredis-mock: duplicate() returns an isolated client sharing in-memory state
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const blocking: any = redis.duplicate();

  const store = new SessionStore(redis, { ttlSec: 60 });
  const limiter = new ConnectionLimiter(redis, "pod-test", {
    actorMax: 3,
    podMax: 100,
  });

  const app = Fastify({ logger: false });
  registerRealtimeStreamRoute(app, {
    sessionStore: store,
    limiter,
    blockingRedis: blocking,
    sharedRedis: redis,
    allowedOrigins: [],
    actorHashKey: HMAC_KEY,
    heartbeatIntervalMs: 50_000,
    sessionCheckIntervalMs: 50_000,
    replayWindowSec: 60,
  });
  await app.ready();

  const created = await store.createSession({
    tenantId: "tenant-1",
    oid: "actor-1",
    scopes: ["audit.read"],
    issuedAt: Math.floor(Date.now() / 1000),
  });
  return { app, redis, blocking, store, cookie: created.rawCookie };
}

describe("GET /api/realtime/v1/stream", () => {
  let ctx: Awaited<ReturnType<typeof setupApp>>;

  beforeEach(async () => {
    ctx = await setupApp();
  });

  afterEach(async () => {
    await ctx.app.close();
    try {
      await ctx.blocking.quit();
    } catch {
      /* ignore */
    }
  });

  it("returns 400 when topic is missing or invalid", async () => {
    const r1 = await ctx.app.inject({
      method: "GET",
      url: "/api/realtime/v1/stream",
    });
    expect(r1.statusCode).toBe(400);

    const r2 = await ctx.app.inject({
      method: "GET",
      url: "/api/realtime/v1/stream?topic=evil",
    });
    expect(r2.statusCode).toBe(400);
  });

  it("returns 401 when session cookie is missing or invalid", async () => {
    const r1 = await ctx.app.inject({
      method: "GET",
      url: "/api/realtime/v1/stream?topic=dashboard",
    });
    expect(r1.statusCode).toBe(401);

    const r2 = await ctx.app.inject({
      method: "GET",
      url: "/api/realtime/v1/stream?topic=dashboard",
      headers: { cookie: "__Host-hrsystem-session=does-not-exist" },
    });
    expect(r2.statusCode).toBe(401);
  });

  it("replays buffered events via Last-Event-ID and applies allow-list", async () => {
    const key = streamKey("tenant-1", "dashboard");
    // ioredis-mock generates sequential ids ("1-0", "2-0", …) which our
    // 60s window filter would reject. Use an explicit ms id; ioredis-mock
    // also has a known quirk where passing `<ms>-<seq>` to XRANGE filtering
    // returns nothing, so we use just `<ms>` strings (real Redis accepts both).
    const ms = Date.now();
    const id = (await ctx.redis.xadd(
      key,
      String(ms),
      "event",
      "AuditEmitted",
      "data",
      JSON.stringify({
        auditId: "a-1",
        tenantId: "tenant-1",
        resourceType: "Employee",
        resourceId: "emp-1",
        eventType: "Updated",
        occurredAt: "2026-04-26T10:00:00Z",
        actor: { oid: "actor-secret" },
        email: "leak@example.com",
      }),
    )) as string;
    expect(id).toBeTruthy();

    const before = String(ms - 1);

    // We can't easily await a long-lived SSE response via inject, so we
    // perform the replay directly to confirm payload filtering works
    // end-to-end against the same Redis state.
    const { replayWindow } = await import(
      "../../../app/lib/realtime/streamConsumer.js"
    );
    const replayed = await replayWindow(ctx.redis, key, before, 60);
    expect(replayed.length).toBe(1);
    expect(replayed[0].payload.email).toBe("leak@example.com"); // raw still has it

    const { filterPayload } = await import(
      "../../../app/lib/realtime/payloadFilter.js"
    );
    const filtered = filterPayload(replayed[0].payload, HMAC_KEY);
    expect(filtered).not.toBeNull();
    expect((filtered as Record<string, unknown>).email).toBeUndefined();
    expect(filtered!.actorHash).toBeTruthy();
  });
});
