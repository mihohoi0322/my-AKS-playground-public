import { createHash, randomBytes } from "node:crypto";
import type { Redis } from "ioredis";

/**
 * Opaque-id session store backed by Redis.
 *
 * The raw cookie value (256-bit CSPRNG, base64url) is sent to the client.
 * Only its SHA-256 hash is stored in Redis under `session:{hash}`, so a
 * Redis dump or read-only compromise cannot impersonate a user.
 */
export interface SessionPayload {
  /** Tenant scope; used for stream-key isolation. */
  tenantId: string;
  /** Entra ID Object ID of the actor. Never leaked to SSE payloads. */
  oid: string;
  /** Granted scopes (e.g. ["audit.read", "approvals.read"]). */
  scopes: readonly string[];
  /** Underlying access-token expiry (epoch seconds). */
  jwtExp?: number;
  /** Optional refresh token for upstream IdP. Opaque. */
  refreshToken?: string;
  /** Issued-at (epoch seconds). */
  issuedAt: number;
}

export interface CreatedSession {
  /** Raw cookie value to send via Set-Cookie. */
  rawCookie: string;
  /** Same value, exposed as sessionId for callers that need to log it. */
  sessionId: string;
  /** SHA-256 hex digest used as the Redis key suffix. */
  sessionHash: string;
}

export interface SessionStoreOptions {
  /** Absolute TTL in seconds. Phase 1 lab default = 1 hour. */
  ttlSec?: number;
}

const DEFAULT_TTL_SEC = 60 * 60;
const RAW_COOKIE_BYTES = 32; // 256 bits

function sha256Hex(value: string): string {
  return createHash("sha256").update(value).digest("hex");
}

function buildKey(hash: string): string {
  return `session:${hash}`;
}

/**
 * Generate a CSPRNG-backed opaque id encoded as base64url (no padding).
 */
export function generateRawCookie(): string {
  return randomBytes(RAW_COOKIE_BYTES).toString("base64url");
}

export class SessionStore {
  private readonly redis: Redis;
  private readonly ttlSec: number;

  constructor(redis: Redis, options: SessionStoreOptions = {}) {
    this.redis = redis;
    this.ttlSec = options.ttlSec ?? DEFAULT_TTL_SEC;
  }

  async createSession(payload: SessionPayload): Promise<CreatedSession> {
    const rawCookie = generateRawCookie();
    const sessionHash = sha256Hex(rawCookie);
    await this.redis.set(
      buildKey(sessionHash),
      JSON.stringify(payload),
      "EX",
      this.ttlSec,
    );
    return { rawCookie, sessionId: rawCookie, sessionHash };
  }

  async lookup(rawCookie: string): Promise<SessionPayload | null> {
    if (!rawCookie) return null;
    const hash = sha256Hex(rawCookie);
    const raw = await this.redis.get(buildKey(hash));
    if (!raw) return null;
    try {
      return JSON.parse(raw) as SessionPayload;
    } catch {
      return null;
    }
  }

  /** Refresh the absolute TTL without reading the payload. */
  async touch(rawCookie: string): Promise<boolean> {
    if (!rawCookie) return false;
    const hash = sha256Hex(rawCookie);
    const result = await this.redis.expire(buildKey(hash), this.ttlSec);
    return result === 1;
  }

  async revoke(rawCookie: string): Promise<void> {
    if (!rawCookie) return;
    const hash = sha256Hex(rawCookie);
    await this.redis.del(buildKey(hash));
  }

  /**
   * Atomic-ish rotation: revoke the old cookie and issue a new one.
   * The two operations are not transactional but the new cookie is only
   * exposed after the old DEL completes.
   */
  async rotate(
    oldRawCookie: string,
    newPayload: SessionPayload,
  ): Promise<CreatedSession> {
    await this.revoke(oldRawCookie);
    return this.createSession(newPayload);
  }
}
