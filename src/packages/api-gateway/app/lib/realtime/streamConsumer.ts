import type { Redis } from "ioredis";

/**
 * Dedicated-connection Redis Streams consumer for SSE.
 *
 * `XREAD BLOCK` ties up a single connection — we therefore call
 * `ioredis.duplicate()` from the caller and pass the dedicated client in.
 * The shared pool is reserved for XADD / XRANGE / SET / GET.
 */
export interface StreamMessage {
  /** Redis stream ID, e.g. "1714080000000-0" */
  id: string;
  /** Decoded JSON payload (raw, before allow-list filtering). */
  payload: Record<string, unknown>;
  /** Event type, defaulted to "message" if missing. */
  eventType: string;
}

export interface StartOptions {
  blockMs?: number;
  /** Last seen stream id (e.g. from Last-Event-ID header). Defaults to "$". */
  startId?: string;
}

export function streamKey(tenantId: string, topic: string): string {
  return `realtime:tenant:${tenantId}:topic:${topic}`;
}

/**
 * Convert a `XREAD` / `XRANGE` Redis reply entry into a {@link StreamMessage}.
 * The producer is expected to encode payload under field `data` as JSON.
 */
export function decodeStreamEntry(
  id: string,
  fields: string[],
): StreamMessage | null {
  let dataRaw: string | undefined;
  let eventType = "message";
  for (let i = 0; i < fields.length; i += 2) {
    const k = fields[i];
    const v = fields[i + 1];
    if (k === "data") dataRaw = v;
    else if (k === "event") eventType = v;
  }
  if (!dataRaw) return null;
  try {
    const payload = JSON.parse(dataRaw) as Record<string, unknown>;
    return { id, payload, eventType };
  } catch {
    return null;
  }
}

/** Convert epoch-ms stream id (e.g. "1714080000000-0") to ms. */
export function streamIdToMs(id: string): number {
  const dashIdx = id.indexOf("-");
  const head = dashIdx >= 0 ? id.slice(0, dashIdx) : id;
  const n = Number(head);
  return Number.isFinite(n) ? n : 0;
}

/**
 * Replay events newer than `lastEventId` and not older than `windowSec`.
 * Uses `XRANGE (lastEventId +` (exclusive start) to avoid re-emitting the
 * cursor itself.
 */
export async function replayWindow(
  redis: Redis,
  key: string,
  lastEventId: string,
  windowSec: number,
): Promise<StreamMessage[]> {
  const cutoffMs = Date.now() - windowSec * 1000;
  if (streamIdToMs(lastEventId) < cutoffMs) {
    // Cursor is older than the replay window → no useful replay.
    return [];
  }
  // Use inclusive XRANGE then drop the cursor entry. Some clients (and the
  // ioredis-mock test stub) do not support the `(` exclusive-range prefix.
  const raw = (await redis.xrange(key, lastEventId, "+")) as Array<
    [string, string[]]
  >;
  const out: StreamMessage[] = [];
  for (const [id, fields] of raw) {
    if (id === lastEventId) continue;
    if (streamIdToMs(id) < cutoffMs) continue;
    const msg = decodeStreamEntry(id, fields);
    if (msg) out.push(msg);
  }
  return out;
}

/**
 * Async generator that yields messages until {@link AbortSignal} fires.
 * Call site is expected to await each yielded message before continuing
 * so that backpressure (`writableNeedDrain`) can pause the loop.
 */
export async function* readStream(
  redis: Redis,
  key: string,
  signal: AbortSignal,
  options: StartOptions = {},
): AsyncGenerator<StreamMessage, void, void> {
  const blockMs = options.blockMs ?? 5000;
  let lastId = options.startId ?? "$";

  while (!signal.aborted) {
    let reply: Array<[string, Array<[string, string[]]>]> | null;
    try {
      // ioredis returns null on BLOCK timeout
      reply = (await redis.xread(
        "BLOCK",
        blockMs,
        "STREAMS",
        key,
        lastId,
      )) as Array<[string, Array<[string, string[]]>]> | null;
    } catch (err) {
      if (signal.aborted) return;
      throw err;
    }
    if (!reply) continue;
    for (const [, entries] of reply) {
      for (const [id, fields] of entries) {
        lastId = id;
        const msg = decodeStreamEntry(id, fields);
        if (msg) yield msg;
        if (signal.aborted) return;
      }
    }
  }
}
