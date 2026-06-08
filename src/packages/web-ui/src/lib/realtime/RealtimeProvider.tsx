"use client";

/**
 * RealtimeProvider — client-side SSE multiplexer.
 *
 * Design summary (see logs/discussion/2026-04-26-w2-design-decisions.md, decision 2):
 * - **Single EventSource per app**: child hooks subscribe via `useRealtimeEvents(topic, handler)`,
 *   and the provider opens ONE connection whose URL includes the union of all active topics.
 *   Rationale: ADR-016 caps SSE concurrency at ~500 connections/Pod; collapsing N topics
 *   into 1 connection avoids burning that budget.
 * - **Visibility-aware**: closes the stream when the tab is hidden and reopens on visible.
 *   Saves Pod connection slots while the user is away.
 * - **Exponential backoff + jitter** (1s → 30s, ±500ms) for reconnect after error.
 * - **StrictMode-safe**: `cancelled` flag + cleanup prevents the dev double-mount from leaving
 *   orphan connections or scheduling reconnects after unmount.
 * - **Last-Event-ID** is delegated to the browser's native EventSource implementation; we never
 *   serialize it manually (api-spec §7.3).
 * - **8KB payload guard**: defensive client-side check; oversized payloads are dropped with a
 *   warning. Server is the source of truth (api-spec §7.2) but a defective sender shouldn't
 *   break the UI.
 * - **`event: auth-expired`** triggers a hard redirect to `/login` (per ADR-017 / discussion).
 *
 * Limitations (W2 scope):
 * - There is no topic field in the SSE payload (api-spec §7.2 only carries `event:`/`id:`/`data:`).
 *   We therefore dispatch every received event to ALL handlers of every currently-active topic;
 *   handlers MUST filter by `event.type`. Future-proofing: if `data.topic` is present we route
 *   by that; otherwise broadcast.
 * - api-gateway carries comma-separated topics (W2-C). If that endpoint isn't ready yet,
 *   set `perTopicConnections={true}` to open one connection per topic instead.
 */

import {
  createContext,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import type {
  RealtimeContextValue,
  RealtimeEvent,
  RealtimeHandler,
  RealtimeStatus,
  RealtimeTopic,
} from "./types";

export const RealtimeContext = createContext<RealtimeContextValue | null>(null);

const MAX_PAYLOAD_BYTES = 8 * 1024;
const BASE_BACKOFF_MS = 1000;
const MAX_BACKOFF_MS = 30_000;
const JITTER_MS = 500;
const DEFAULT_STREAM_PATH = "/api/realtime/v1/stream";
const DEFAULT_LOGIN_URL = "/login";

export interface RealtimeProviderProps {
  children: ReactNode;
  /** Override the SSE endpoint (mainly for tests). */
  streamPath?: string;
  /** Override the redirect target on `auth-expired`. */
  loginUrl?: string;
  /**
   * Force one-connection-per-topic mode. Defaults to `false` (single multiplexed connection).
   * Set to `true` until api-gateway W2-C ships comma-separated topic support.
   */
  perTopicConnections?: boolean;
}

interface ConnectionHandle {
  source: EventSource;
}

type ConnState = "connecting" | "open" | "closed" | "auth-expired";

export function RealtimeProvider({
  children,
  streamPath = DEFAULT_STREAM_PATH,
  loginUrl = DEFAULT_LOGIN_URL,
  perTopicConnections = false,
}: RealtimeProviderProps): ReactNode {
  // Map<topic, Set<handler>>. Plain Map keeps reference stable; we publish the sorted topic
  // membership through `topicKey` state so the connection effect can react.
  const subscribersRef = useRef<Map<RealtimeTopic, Set<RealtimeHandler>>>(new Map());
  const [topicKey, setTopicKey] = useState<string>("");
  // connState transitions are driven exclusively by async event handlers (onopen, onerror,
  // visibilitychange, auth-expired), never synchronously inside the connection effect's body
  // — that would trigger React's `set-state-in-effect` cascade warning.
  const [connState, setConnState] = useState<ConnState>("connecting");
  const connStateRef = useRef<ConnState>("connecting");
  const setConn = useCallback((next: ConnState) => {
    connStateRef.current = next;
    setConnState(next);
  }, []);

  const recomputeTopicKey = useCallback(() => {
    const next = [...subscribersRef.current.keys()].sort().join(",");
    setTopicKey((prev) => (prev === next ? prev : next));
  }, []);

  const subscribe = useCallback<RealtimeContextValue["subscribe"]>(
    (topic, handler) => {
      const map = subscribersRef.current;
      let set = map.get(topic);
      if (!set) {
        set = new Set();
        map.set(topic, set);
      }
      set.add(handler);
      recomputeTopicKey();
      return () => {
        const current = subscribersRef.current.get(topic);
        if (!current) return;
        current.delete(handler);
        if (current.size === 0) {
          subscribersRef.current.delete(topic);
        }
        recomputeTopicKey();
      };
    },
    [recomputeTopicKey],
  );

  const dispatch = useCallback((event: RealtimeEvent) => {
    // 8KB defensive guard — measured on the raw `data` JSON since that is what api-spec §7.2 caps.
    let payloadSize = 0;
    if (event.data !== null) {
      try {
        payloadSize = JSON.stringify(event.data).length;
      } catch {
        payloadSize = 0;
      }
    }
    if (payloadSize > MAX_PAYLOAD_BYTES) {
      console.warn(
        `[realtime] dropping ${event.type} event: payload ${payloadSize}B exceeds ${MAX_PAYLOAD_BYTES}B cap`,
      );
      return;
    }

    // Future-proof routing: if payload carries an explicit topic, fan-out only to that topic's
    // subscribers. Otherwise broadcast to every active subscriber (handlers self-filter by type).
    const map = subscribersRef.current;
    const explicitTopic =
      event.data && typeof event.data === "object" && "topic" in event.data
        ? (event.data as { topic?: unknown }).topic
        : undefined;
    if (typeof explicitTopic === "string" && map.has(explicitTopic)) {
      const handlers = map.get(explicitTopic);
      if (handlers) {
        for (const h of handlers) {
          try {
            h({ ...event, topic: explicitTopic });
          } catch (err) {
            console.error("[realtime] subscriber threw", err);
          }
        }
      }
      return;
    }
    for (const [topic, handlers] of map) {
      for (const h of handlers) {
        try {
          h({ ...event, topic });
        } catch (err) {
          console.error("[realtime] subscriber threw", err);
        }
      }
    }
  }, []);

  useEffect(() => {
    if (typeof window === "undefined") return;
    if (typeof EventSource === "undefined") return;
    if (topicKey === "") return;

    let cancelled = false;
    let attempt = 0;
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
    let connections: ConnectionHandle[] = [];
    const topics = topicKey.split(",");

    const closeAll = (): void => {
      for (const c of connections) {
        try {
          c.source.close();
        } catch {
          /* noop */
        }
      }
      connections = [];
    };

    const scheduleReconnect = (): void => {
      if (cancelled) return;
      attempt += 1;
      const expo = Math.min(BASE_BACKOFF_MS * 2 ** (attempt - 1), MAX_BACKOFF_MS);
      const jitter = Math.floor((Math.random() * 2 - 1) * JITTER_MS);
      const delay = Math.max(0, expo + jitter);
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null;
        connect();
      }, delay);
    };

    const handleSourceMessage = (
      ev: MessageEvent,
      eventName: string,
      connectionTopic: string | null,
    ): void => {
      if (cancelled) return;
      let parsed: unknown = null;
      if (typeof ev.data === "string" && ev.data.length > 0) {
        try {
          parsed = JSON.parse(ev.data) as unknown;
        } catch {
          parsed = ev.data;
        }
      }
      const event: RealtimeEvent = {
        type: eventName,
        data: parsed,
        lastEventId: ev.lastEventId ?? "",
        topic: connectionTopic ?? "",
      };
      dispatch(event);
    };

    const knownEventNames = new Set<string>([
      "dashboardUpdated",
      "auditCreated",
      "notificationReceived",
    ]);

    const wireSource = (source: EventSource, connectionTopic: string | null): void => {
      source.onopen = (): void => {
        if (cancelled) return;
        attempt = 0;
        setConn("open");
      };
      source.onmessage = (ev): void => handleSourceMessage(ev, "message", connectionTopic);
      for (const name of knownEventNames) {
        source.addEventListener(name, (ev) =>
          handleSourceMessage(ev as MessageEvent, name, connectionTopic),
        );
      }
      source.addEventListener("auth-expired", () => {
        if (cancelled) return;
        setConn("auth-expired");
        closeAll();
        if (typeof window !== "undefined") {
          window.location.href = loginUrl;
        }
      });
      source.onerror = (): void => {
        if (cancelled) return;
        if (connStateRef.current === "auth-expired") return;
        // The browser auto-reconnects on transient errors; we close + back off ourselves to
        // get jitter and respect visibility transitions deterministically.
        closeAll();
        setConn("connecting");
        scheduleReconnect();
      };
    };

    const connect = (): void => {
      if (cancelled) return;
      closeAll();
      try {
        if (perTopicConnections) {
          for (const t of topics) {
            const url = `${streamPath}?topic=${encodeURIComponent(t)}`;
            const src = new EventSource(url, { withCredentials: true });
            connections.push({ source: src });
            wireSource(src, t);
          }
        } else {
          const url = `${streamPath}?topic=${topics.map((t) => encodeURIComponent(t)).join(",")}`;
          const src = new EventSource(url, { withCredentials: true });
          connections.push({ source: src });
          wireSource(src, null);
        }
      } catch (err) {
        console.error("[realtime] failed to open EventSource", err);
        scheduleReconnect();
      }
    };

    const handleVisibility = (): void => {
      if (cancelled) return;
      if (document.visibilityState === "hidden") {
        if (reconnectTimer) {
          clearTimeout(reconnectTimer);
          reconnectTimer = null;
        }
        closeAll();
        setConn("closed");
      } else if (document.visibilityState === "visible") {
        if (connStateRef.current === "auth-expired") return;
        attempt = 0;
        setConn("connecting");
        connect();
      }
    };

    if (typeof document === "undefined" || document.visibilityState !== "hidden") {
      connect();
    }
    // If hidden at mount, we wait for visibilitychange to become visible. The default connState
    // is "connecting" which is benign until the user returns to the tab.

    if (typeof document !== "undefined") {
      document.addEventListener("visibilitychange", handleVisibility);
    }

    return (): void => {
      cancelled = true;
      if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimer = null;
      }
      if (typeof document !== "undefined") {
        document.removeEventListener("visibilitychange", handleVisibility);
      }
      closeAll();
    };
  }, [topicKey, streamPath, loginUrl, perTopicConnections, dispatch, setConn]);

  const status: RealtimeStatus = topicKey === "" ? "idle" : connState;
  const value = useMemo<RealtimeContextValue>(() => ({ subscribe, status }), [subscribe, status]);

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}
