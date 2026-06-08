import { metrics, type Counter, type UpDownCounter } from "@opentelemetry/api";

let _connectionsActive: UpDownCounter | null = null;
let _replayHits: Counter | null = null;
let _replayMisses: Counter | null = null;
let _droppedPayloads: Counter | null = null;
let _sessionRevoked: Counter | null = null;

function ensureMeters(): void {
  if (_connectionsActive) return;
  const meter = metrics.getMeter("aks-hrsystem-lab.realtime", "0.1.0");
  _connectionsActive = meter.createUpDownCounter(
    "audit.realtime.connections_active",
    {
      description: "Active SSE connections on this Pod",
    },
  );
  _replayHits = meter.createCounter("audit.realtime.replay_hits", {
    description:
      "Last-Event-ID replays that found at least one event in the buffer",
  });
  _replayMisses = meter.createCounter("audit.realtime.replay_misses", {
    description: "Last-Event-ID replays that fell outside the 60s window",
  });
  _droppedPayloads = meter.createCounter(
    "audit.realtime.dropped_payloads",
    {
      description:
        "Payloads dropped because allow-list filter rejected required fields",
    },
  );
  _sessionRevoked = meter.createCounter(
    "audit.realtime.session_revoked_during_stream",
    {
      description:
        "SSE streams closed because the backing session disappeared mid-stream",
    },
  );
}

export function incConnections(delta: number, attrs?: Record<string, string>): void {
  ensureMeters();
  _connectionsActive!.add(delta, attrs);
}

export function recordReplayHit(attrs?: Record<string, string>): void {
  ensureMeters();
  _replayHits!.add(1, attrs);
}

export function recordReplayMiss(attrs?: Record<string, string>): void {
  ensureMeters();
  _replayMisses!.add(1, attrs);
}

export function recordDroppedPayload(attrs?: Record<string, string>): void {
  ensureMeters();
  _droppedPayloads!.add(1, attrs);
}

export function recordSessionRevokedDuringStream(
  attrs?: Record<string, string>,
): void {
  ensureMeters();
  _sessionRevoked!.add(1, attrs);
}
