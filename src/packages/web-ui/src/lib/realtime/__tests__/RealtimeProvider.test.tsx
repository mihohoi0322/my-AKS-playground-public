/**
 * @vitest-environment jsdom
 */
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { act, render, renderHook } from "@testing-library/react";
import React from "react";
import { RealtimeProvider } from "../RealtimeProvider";
import { useRealtimeEvents, useRealtimeStatus } from "../useRealtimeEvents";
import type { RealtimeEvent } from "../types";

// ---- EventSource mock --------------------------------------------------------
type Listener = (ev: MessageEvent) => void;

class MockEventSource {
  static instances: MockEventSource[] = [];
  static CONNECTING = 0 as const;
  static OPEN = 1 as const;
  static CLOSED = 2 as const;

  url: string;
  withCredentials: boolean;
  readyState: number = MockEventSource.CONNECTING;
  closed = false;

  onopen: ((ev: Event) => void) | null = null;
  onmessage: ((ev: MessageEvent) => void) | null = null;
  onerror: ((ev: Event) => void) | null = null;
  private listeners: Map<string, Set<Listener>> = new Map();

  constructor(url: string | URL, init?: EventSourceInit) {
    this.url = String(url);
    this.withCredentials = init?.withCredentials ?? false;
    MockEventSource.instances.push(this);
  }

  addEventListener(type: string, fn: Listener): void {
    let set = this.listeners.get(type);
    if (!set) {
      set = new Set();
      this.listeners.set(type, set);
    }
    set.add(fn);
  }

  removeEventListener(type: string, fn: Listener): void {
    this.listeners.get(type)?.delete(fn);
  }

  close(): void {
    this.closed = true;
    this.readyState = MockEventSource.CLOSED;
  }

  // Test helpers
  emitOpen(): void {
    this.readyState = MockEventSource.OPEN;
    this.onopen?.(new Event("open"));
  }
  emitMessage(eventName: string, data: unknown, id = ""): void {
    const ev = new MessageEvent(eventName, {
      data: typeof data === "string" ? data : JSON.stringify(data),
      lastEventId: id,
    });
    if (eventName === "message") {
      this.onmessage?.(ev);
    }
    this.listeners.get(eventName)?.forEach((fn) => fn(ev));
  }
  emitError(): void {
    this.onerror?.(new Event("error"));
  }
}

beforeEach(() => {
  MockEventSource.instances = [];
  vi.stubGlobal("EventSource", MockEventSource as unknown as typeof EventSource);
  // Force the visibility branch to "visible" by default.
  Object.defineProperty(document, "visibilityState", {
    configurable: true,
    get: () => "visible",
  });
  vi.useFakeTimers();
  vi.spyOn(Math, "random").mockReturnValue(0.5); // jitter = 0
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function fireVisibility(state: "visible" | "hidden"): void {
  Object.defineProperty(document, "visibilityState", {
    configurable: true,
    get: () => state,
  });
  document.dispatchEvent(new Event("visibilitychange"));
}

// ---- Tests ------------------------------------------------------------------

describe("RealtimeProvider", () => {
  it("opens a single EventSource that multiplexes all subscribed topics", () => {
    const handlerA = vi.fn();
    const handlerB = vi.fn();
    const Wrapper = ({ children }: { children: React.ReactNode }): React.ReactElement => (
      <RealtimeProvider>{children}</RealtimeProvider>
    );
    renderHook(
      () => {
        useRealtimeEvents("dashboard", handlerA);
        useRealtimeEvents("notifications", handlerB);
      },
      { wrapper: Wrapper },
    );

    // Flush the microtask + effect queue.
    act(() => {
      vi.runOnlyPendingTimers();
    });

    expect(MockEventSource.instances.length).toBe(1);
    const url = MockEventSource.instances[0]!.url;
    expect(url).toContain("/api/realtime/v1/stream?topic=");
    // Topics are sorted alphabetically.
    expect(url).toMatch(/topic=dashboard,notifications$/);
  });

  it("does not leak connections under StrictMode double-mount", () => {
    const Wrapper = ({ children }: { children: React.ReactNode }): React.ReactElement => (
      <React.StrictMode>
        <RealtimeProvider>{children}</RealtimeProvider>
      </React.StrictMode>
    );
    renderHook(() => useRealtimeEvents("dashboard", () => {}), { wrapper: Wrapper });

    act(() => {
      vi.runOnlyPendingTimers();
    });

    // After StrictMode double-mount: at most one open (non-closed) EventSource at a time.
    const live = MockEventSource.instances.filter((es) => !es.closed);
    expect(live.length).toBeLessThanOrEqual(1);
  });

  it("closes on visibilitychange=hidden and reopens on visible", () => {
    const Wrapper = ({ children }: { children: React.ReactNode }): React.ReactElement => (
      <RealtimeProvider>{children}</RealtimeProvider>
    );
    renderHook(() => useRealtimeEvents("dashboard", () => {}), { wrapper: Wrapper });

    act(() => {
      vi.runOnlyPendingTimers();
    });
    expect(MockEventSource.instances.length).toBe(1);
    const first = MockEventSource.instances[0]!;

    act(() => {
      fireVisibility("hidden");
    });
    expect(first.closed).toBe(true);

    act(() => {
      fireVisibility("visible");
      vi.runOnlyPendingTimers();
    });
    expect(MockEventSource.instances.length).toBe(2);
    expect(MockEventSource.instances[1]!.closed).toBe(false);
  });

  it("uses exponential backoff with jitter on error", () => {
    const Wrapper = ({ children }: { children: React.ReactNode }): React.ReactElement => (
      <RealtimeProvider>{children}</RealtimeProvider>
    );
    renderHook(() => useRealtimeEvents("dashboard", () => {}), { wrapper: Wrapper });

    act(() => {
      vi.runOnlyPendingTimers();
    });
    const es1 = MockEventSource.instances[0]!;

    // First error → backoff = 1000ms (jitter=0 because Math.random=0.5 → 2*0.5-1 = 0).
    act(() => {
      es1.emitError();
    });
    act(() => {
      vi.advanceTimersByTime(999);
    });
    expect(MockEventSource.instances.length).toBe(1);
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(MockEventSource.instances.length).toBe(2);

    // Second error → 2000ms.
    const es2 = MockEventSource.instances[1]!;
    act(() => {
      es2.emitError();
    });
    act(() => {
      vi.advanceTimersByTime(1999);
    });
    expect(MockEventSource.instances.length).toBe(2);
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(MockEventSource.instances.length).toBe(3);
  });

  it("delivers events to subscribers and the hook unsubscribes on unmount", () => {
    const handler = vi.fn();
    const Wrapper = ({ children }: { children: React.ReactNode }): React.ReactElement => (
      <RealtimeProvider>{children}</RealtimeProvider>
    );
    const { unmount } = renderHook(() => useRealtimeEvents("dashboard", handler), {
      wrapper: Wrapper,
    });

    act(() => {
      vi.runOnlyPendingTimers();
    });
    const es = MockEventSource.instances[0]!;
    act(() => {
      es.emitMessage("dashboardUpdated", { resourceRef: "/x" }, "01HXY...");
    });

    expect(handler).toHaveBeenCalledTimes(1);
    const arg = handler.mock.calls[0]![0] as RealtimeEvent;
    expect(arg.type).toBe("dashboardUpdated");
    expect(arg.lastEventId).toBe("01HXY...");
    expect(arg.topic).toBe("dashboard");

    unmount();
    act(() => {
      // After unmount, the new EventSource (if any) should be closed.
      MockEventSource.instances.forEach((i) => expect(i.closed).toBe(true));
    });
  });

  it("drops events whose payload exceeds 8KB", () => {
    const handler = vi.fn();
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});
    const Wrapper = ({ children }: { children: React.ReactNode }): React.ReactElement => (
      <RealtimeProvider>{children}</RealtimeProvider>
    );
    renderHook(() => useRealtimeEvents("dashboard", handler), { wrapper: Wrapper });
    act(() => {
      vi.runOnlyPendingTimers();
    });

    const es = MockEventSource.instances[0]!;
    const huge = { blob: "x".repeat(9000) };
    act(() => {
      es.emitMessage("dashboardUpdated", huge);
    });

    expect(handler).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalled();
  });

  it("redirects to /login on auth-expired", () => {
    const Wrapper = ({ children }: { children: React.ReactNode }): React.ReactElement => (
      <RealtimeProvider loginUrl="/login">{children}</RealtimeProvider>
    );
    // Stub location.href setter so we can observe the assignment without jsdom navigation.
    const original = window.location;
    let assigned = "";
    Object.defineProperty(window, "location", {
      configurable: true,
      value: {
        ...original,
        get href(): string {
          return assigned;
        },
        set href(v: string) {
          assigned = v;
        },
      },
    });

    renderHook(() => useRealtimeEvents("dashboard", () => {}), { wrapper: Wrapper });
    act(() => {
      vi.runOnlyPendingTimers();
    });
    const es = MockEventSource.instances[0]!;
    act(() => {
      es.emitMessage("auth-expired", {});
    });
    expect(assigned).toBe("/login");

    Object.defineProperty(window, "location", { configurable: true, value: original });
  });
});

describe("useRealtimeEvents", () => {
  it("throws when used outside <RealtimeProvider>", () => {
    // Suppress React's error log noise during expected throw.
    const errSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => renderHook(() => useRealtimeEvents("dashboard", () => {}))).toThrow(
      /must be used inside a <RealtimeProvider>/,
    );
    errSpy.mockRestore();
  });
});

describe("useRealtimeStatus", () => {
  it("returns 'idle' when no topics are subscribed and 'connecting'/'open' once they are", () => {
    function StatusProbe(): React.ReactElement {
      const status = useRealtimeStatus();
      return <div data-testid="status">{status}</div>;
    }
    const { getByTestId, rerender } = render(
      <RealtimeProvider>
        <StatusProbe />
      </RealtimeProvider>,
    );
    expect(getByTestId("status").textContent).toBe("idle");

    function WithSub(): React.ReactElement {
      useRealtimeEvents("dashboard", () => {});
      return <StatusProbe />;
    }
    rerender(
      <RealtimeProvider>
        <WithSub />
      </RealtimeProvider>,
    );
    act(() => {
      vi.runOnlyPendingTimers();
    });
    expect(["connecting", "open"]).toContain(getByTestId("status").textContent);
  });
});
