import { describe, it, expect, beforeEach } from "vitest";
import RedisMock from "ioredis-mock";
import {
  SessionStore,
  generateRawCookie,
} from "../../../app/lib/session/sessionStore.js";

function makePayload() {
  return {
    tenantId: "tenant-1",
    oid: "actor-oid-123",
    scopes: ["audit.read"],
    issuedAt: Math.floor(Date.now() / 1000),
  };
}

describe("SessionStore", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let redis: any;

  beforeEach(async () => {
    redis = new RedisMock();
    await redis.flushall();
  });

  it("creates a session with CSPRNG cookie and SHA-256 hash key", async () => {
    const store = new SessionStore(redis, { ttlSec: 60 });
    const created = await store.createSession(makePayload());

    expect(created.rawCookie).toMatch(/^[A-Za-z0-9_-]+$/);
    expect(created.rawCookie).toBe(created.sessionId);
    expect(created.sessionHash).toMatch(/^[a-f0-9]{64}$/);

    // Raw cookie value MUST NOT exist as a Redis key (only hash should).
    const rawKeyExists = await redis.exists(`session:${created.rawCookie}`);
    expect(rawKeyExists).toBe(0);

    const hashKeyExists = await redis.exists(`session:${created.sessionHash}`);
    expect(hashKeyExists).toBe(1);
  });

  it("looks up an existing session by raw cookie", async () => {
    const store = new SessionStore(redis);
    const created = await store.createSession(makePayload());
    const found = await store.lookup(created.rawCookie);
    expect(found?.tenantId).toBe("tenant-1");
    expect(found?.oid).toBe("actor-oid-123");
  });

  it("returns null for unknown / empty cookies", async () => {
    const store = new SessionStore(redis);
    expect(await store.lookup("")).toBeNull();
    expect(await store.lookup("not-a-real-id")).toBeNull();
  });

  it("touch refreshes TTL", async () => {
    const store = new SessionStore(redis, { ttlSec: 60 });
    const created = await store.createSession(makePayload());
    const ok = await store.touch(created.rawCookie);
    expect(ok).toBe(true);

    const ttl = await redis.ttl(`session:${created.sessionHash}`);
    expect(ttl).toBeGreaterThan(0);
  });

  it("revoke removes the session", async () => {
    const store = new SessionStore(redis);
    const created = await store.createSession(makePayload());
    await store.revoke(created.rawCookie);
    expect(await store.lookup(created.rawCookie)).toBeNull();
  });

  it("rotate revokes the old cookie and issues a new one", async () => {
    const store = new SessionStore(redis);
    const original = await store.createSession(makePayload());

    const rotated = await store.rotate(original.rawCookie, {
      ...makePayload(),
      scopes: ["audit.read", "approvals.read"],
    });

    expect(await store.lookup(original.rawCookie)).toBeNull();
    const found = await store.lookup(rotated.rawCookie);
    expect(found?.scopes).toContain("approvals.read");
    expect(rotated.rawCookie).not.toBe(original.rawCookie);
  });

  it("generateRawCookie produces 256-bit base64url tokens", () => {
    const a = generateRawCookie();
    const b = generateRawCookie();
    expect(a).not.toBe(b);
    // 32 bytes → ceil(32 * 4 / 3) = 43 base64url chars (no padding)
    expect(a.length).toBeGreaterThanOrEqual(43);
  });
});
