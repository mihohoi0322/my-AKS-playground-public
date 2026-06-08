import { describe, it, expect, beforeEach } from "vitest";
import RedisMock from "ioredis-mock";
import { ConnectionLimiter } from "../../../app/lib/realtime/connectionLimiter.js";

describe("ConnectionLimiter", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let redis: any;

  beforeEach(async () => {
    redis = new RedisMock();
    // ioredis-mock shares in-memory state across instances by default.
    await redis.flushall();
  });

  it("allows up to actorMax concurrent connections, then rejects", async () => {
    const limiter = new ConnectionLimiter(redis, "pod-1", {
      actorMax: 3,
      podMax: 100,
    });
    const oid = "actor-1";

    for (let i = 0; i < 3; i++) {
      const r = await limiter.acquire(oid, `c-${i}`);
      expect(r.ok).toBe(true);
    }

    const denied = await limiter.acquire(oid, "c-overflow");
    expect(denied.ok).toBe(false);
    if (!denied.ok) {
      expect(denied.reason).toBe("actor");
      expect(denied.limit).toBe(3);
    }
  });

  it("rejects when podMax reached", async () => {
    const limiter = new ConnectionLimiter(redis, "pod-1", {
      actorMax: 100,
      podMax: 2,
    });

    expect((await limiter.acquire("a", "c1")).ok).toBe(true);
    expect((await limiter.acquire("b", "c2")).ok).toBe(true);

    const denied = await limiter.acquire("c", "c3");
    expect(denied.ok).toBe(false);
    if (!denied.ok) {
      expect(denied.reason).toBe("pod");
    }
  });

  it("prunes stale entries via ZREMRANGEBYSCORE", async () => {
    const limiter = new ConnectionLimiter(redis, "pod-1", {
      actorMax: 3,
      podMax: 100,
      staleAfterSec: 30,
    });
    const oid = "actor-stale";

    // Pre-populate the actor zset with 3 entries scored well outside the
    // 30s window so they look dead.
    const ancient = Math.floor(Date.now() / 1000) - 600;
    await redis.zadd(`sse:actor:${oid}`, ancient, "old-1");
    await redis.zadd(`sse:actor:${oid}`, ancient, "old-2");
    await redis.zadd(`sse:actor:${oid}`, ancient, "old-3");

    const r = await limiter.acquire(oid, "fresh-1");
    expect(r.ok).toBe(true);

    // Only the fresh entry should remain
    const card = await redis.zcard(`sse:actor:${oid}`);
    expect(card).toBe(1);
  });

  it("release removes the entry from the zset", async () => {
    const limiter = new ConnectionLimiter(redis, "pod-1");
    await limiter.acquire("a", "c-x");
    await limiter.release("a", "c-x");
    const card = await redis.zcard("sse:actor:a");
    expect(card).toBe(0);
  });

  it("heartbeat refreshes the zset score", async () => {
    const limiter = new ConnectionLimiter(redis, "pod-1");
    await limiter.acquire("a", "c-x");
    const before = (await redis.zscore("sse:actor:a", "c-x")) as string;
    await new Promise((r) => setTimeout(r, 1100));
    await limiter.heartbeat("a", "c-x");
    const after = (await redis.zscore("sse:actor:a", "c-x")) as string;
    expect(Number(after)).toBeGreaterThanOrEqual(Number(before));
  });
});
