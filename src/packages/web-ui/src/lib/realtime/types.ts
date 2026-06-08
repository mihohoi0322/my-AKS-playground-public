/**
 * Realtime types shared between Provider and hook.
 *
 * @see docs/api-spec.md §7
 * @see docs/frontend-design.md §1.5
 */

/** Connection lifecycle states surfaced by the Provider. */
export type RealtimeStatus = "idle" | "connecting" | "open" | "closed" | "auth-expired";

/** Allowed topic values (kept loose at the type level; Provider validates at runtime via server). */
export type RealtimeTopic = string;

/** Event delivered to subscriber handlers. */
export interface RealtimeEvent<T = unknown> {
  /** SSE `event:` field (lower camelCase, e.g. `dashboardUpdated`). */
  type: string;
  /** Parsed JSON payload. `null` when payload was non-JSON or empty. */
  data: T | null;
  /** SSE `id:` (ULID). Empty string if missing. */
  lastEventId: string;
  /** Topic this event was dispatched against. */
  topic: RealtimeTopic;
}

export type RealtimeHandler<T = unknown> = (event: RealtimeEvent<T>) => void;

export interface RealtimeContextValue {
  /**
   * Register a handler for `topic`. Returns an unsubscribe function.
   * Adding the first handler for a topic triggers (re)connection so the URL
   * includes the new topic. Removing the last handler for a topic does NOT
   * eagerly close the stream — it remains until all subscribers leave.
   */
  subscribe: (topic: RealtimeTopic, handler: RealtimeHandler) => () => void;
  /** Current connection state. */
  status: RealtimeStatus;
}
