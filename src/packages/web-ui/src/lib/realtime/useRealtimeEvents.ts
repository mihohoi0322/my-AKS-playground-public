"use client";

import { useContext, useEffect } from "react";
import { RealtimeContext } from "./RealtimeProvider";
import type { RealtimeHandler, RealtimeTopic } from "./types";

/**
 * Subscribe to realtime events for `topic`.
 *
 * Wrap `handler` in `useCallback` (or define it outside the component) — the subscription
 * is re-registered whenever `handler`'s identity changes, which forces a brief unsubscribe/
 * re-subscribe round-trip even though the underlying EventSource stays open.
 *
 * Throws if used outside a `<RealtimeProvider>`.
 *
 * @example TanStack Query integration (planned for W3):
 * ```ts
 * const queryClient = useQueryClient();
 * useRealtimeEvents("dashboard", useCallback((ev) => {
 *   if (ev.type === "auditCreated") {
 *     queryClient.invalidateQueries({ queryKey: ["audit"] });
 *   } else if (ev.type === "dashboardUpdated") {
 *     queryClient.invalidateQueries({ queryKey: ["dashboard"] });
 *   }
 * }, [queryClient]));
 * ```
 */
export function useRealtimeEvents<T = unknown>(topic: RealtimeTopic, handler: RealtimeHandler<T>): void {
  const ctx = useContext(RealtimeContext);
  if (ctx === null) {
    throw new Error("useRealtimeEvents must be used inside a <RealtimeProvider>");
  }
  useEffect(() => {
    const unsubscribe = ctx.subscribe(topic, handler as RealtimeHandler);
    return unsubscribe;
  }, [ctx, topic, handler]);
}

/**
 * Read the current realtime connection status without subscribing to events.
 * Useful for surfacing a "reconnecting…" pill in the header.
 */
export function useRealtimeStatus(): "idle" | "connecting" | "open" | "closed" | "auth-expired" {
  const ctx = useContext(RealtimeContext);
  if (ctx === null) {
    throw new Error("useRealtimeStatus must be used inside a <RealtimeProvider>");
  }
  return ctx.status;
}
