import type { Redis } from "ioredis";

/**
 * SSE connection limiter using Redis ZSET (per-actor) + INCR (per-Pod),
 * with an atomic check-and-add Lua script so multiple Pods never overshoot.
 *
 * Per-actor: `sse:actor:{oid}` ZSET, score = epochSec, member = connectionId.
 *   - Stale entries (>30s without heartbeat) are pruned by ZREMRANGEBYSCORE.
 *   - >= actorMax → reject.
 * Per-Pod: `sse:pod:{podName}` INCR with 60s expire.
 *   - >= podMax → reject.
 */
export interface LimiterAcceptResult {
  ok: true;
  actorCount: number;
  podCount: number;
}

export interface LimiterRejectResult {
  ok: false;
  reason: "actor" | "pod";
  current: number;
  limit: number;
}

export type LimiterResult = LimiterAcceptResult | LimiterRejectResult;

export interface ConnectionLimiterOptions {
  actorMax?: number;
  podMax?: number;
  /** Window in seconds; entries older than this are considered dead. */
  staleAfterSec?: number;
  /** Pod counter TTL in seconds. */
  podTtlSec?: number;
}

const DEFAULTS = {
  actorMax: 3,
  podMax: 500,
  staleAfterSec: 30,
  podTtlSec: 60,
};

// KEYS[1] = actor zset, KEYS[2] = pod counter
// ARGV[1] = connectionId, ARGV[2] = now (sec), ARGV[3] = cutoff (sec)
// ARGV[4] = actorMax, ARGV[5] = podMax, ARGV[6] = podTtlSec, ARGV[7] = actorTtlSec
const ACQUIRE_LUA = `
redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[3])
local actorCount = tonumber(redis.call('ZCARD', KEYS[1]))
if actorCount >= tonumber(ARGV[4]) then
  return {0, 'actor', actorCount, tonumber(ARGV[4])}
end
local podCount = tonumber(redis.call('GET', KEYS[2]) or '0')
if podCount >= tonumber(ARGV[5]) then
  return {0, 'pod', podCount, tonumber(ARGV[5])}
end
redis.call('ZADD', KEYS[1], ARGV[2], ARGV[1])
redis.call('EXPIRE', KEYS[1], ARGV[7])
local newPod = tonumber(redis.call('INCR', KEYS[2]))
redis.call('EXPIRE', KEYS[2], ARGV[6])
return {1, 'ok', actorCount + 1, newPod}
`;

export class ConnectionLimiter {
  private readonly redis: Redis;
  private readonly podName: string;
  private readonly actorMax: number;
  private readonly podMax: number;
  private readonly staleAfterSec: number;
  private readonly podTtlSec: number;

  constructor(
    redis: Redis,
    podName: string,
    options: ConnectionLimiterOptions = {},
  ) {
    this.redis = redis;
    this.podName = podName;
    this.actorMax = options.actorMax ?? DEFAULTS.actorMax;
    this.podMax = options.podMax ?? DEFAULTS.podMax;
    this.staleAfterSec = options.staleAfterSec ?? DEFAULTS.staleAfterSec;
    this.podTtlSec = options.podTtlSec ?? DEFAULTS.podTtlSec;
  }

  private actorKey(oid: string): string {
    return `sse:actor:${oid}`;
  }

  private podKey(): string {
    return `sse:pod:${this.podName}`;
  }

  async acquire(oid: string, connectionId: string): Promise<LimiterResult> {
    const now = Math.floor(Date.now() / 1000);
    const cutoff = now - this.staleAfterSec;
    const actorTtl = this.staleAfterSec * 2;

    // ioredis exposes eval(script, numKeys, ...keys, ...args)
    const result = (await this.redis.eval(
      ACQUIRE_LUA,
      2,
      this.actorKey(oid),
      this.podKey(),
      connectionId,
      String(now),
      String(cutoff),
      String(this.actorMax),
      String(this.podMax),
      String(this.podTtlSec),
      String(actorTtl),
    )) as [number, string, number, number];

    const [okFlag, reason, current, limitOrPod] = result;
    if (okFlag === 1) {
      return {
        ok: true,
        actorCount: current,
        podCount: limitOrPod,
      };
    }
    return {
      ok: false,
      reason: reason === "pod" ? "pod" : "actor",
      current,
      limit: limitOrPod,
    };
  }

  /**
   * Refresh the actor ZSET score for this connection so it is not pruned
   * by ZREMRANGEBYSCORE while the SSE stream is still live.
   */
  async heartbeat(oid: string, connectionId: string): Promise<void> {
    const now = Math.floor(Date.now() / 1000);
    const actorTtl = this.staleAfterSec * 2;
    await this.redis
      .multi()
      .zadd(this.actorKey(oid), now, connectionId)
      .expire(this.actorKey(oid), actorTtl)
      .exec();
  }

  async release(oid: string, connectionId: string): Promise<void> {
    await this.redis.zrem(this.actorKey(oid), connectionId);
    // Pod counter is best-effort; DECR can briefly go negative on race.
    const newVal = await this.redis.decr(this.podKey());
    if (newVal < 0) {
      await this.redis.set(this.podKey(), 0, "EX", this.podTtlSec);
    }
  }
}
