import { createHmac } from "node:crypto";

/**
 * Allow-list filter for Redis Streams payloads emitted via SSE.
 *
 * Per W2 design decision: only an explicit set of identifier-style fields
 * are forwarded to clients. Anything else (including `actor.oid`, names,
 * email, free-form text, IP, UA) is stripped. If `actor.oid` is present
 * we replace it with a tenant-scoped HMAC `actorHash` so consumers can
 * still correlate per-actor events without leaking the raw OID.
 */
export const ALLOWED_FIELDS = [
  "auditId",
  "tenantId",
  "resourceType",
  "resourceId",
  "eventType",
  "occurredAt",
  "scopeKey",
] as const;

export const REQUIRED_FIELDS = [
  "auditId",
  "tenantId",
  "resourceType",
  "resourceId",
  "eventType",
  "occurredAt",
] as const;

export interface FilteredPayload {
  auditId: string;
  tenantId: string;
  resourceType: string;
  resourceId: string;
  eventType: string;
  occurredAt: string;
  scopeKey?: string;
  actorHash?: string;
  // Index signature so the payload can be passed to APIs typed as
  // `Record<string, unknown>` (e.g. JSON serializers).
  [key: string]: unknown;
}

interface RawPayloadMaybe {
  [k: string]: unknown;
  actor?: { oid?: unknown } | null;
}

function actorHash(
  hmacKey: string,
  tenantId: string,
  oid: string,
): string {
  // tenant-scoped: include tenantId in the HMAC message so the same OID in
  // different tenants produces different hashes.
  return createHmac("sha256", hmacKey)
    .update(`${tenantId}:${oid}`)
    .digest("base64url")
    .slice(0, 22); // ~128 bits
}

/**
 * @returns filtered payload, or `null` if a required field is missing.
 */
export function filterPayload(
  raw: unknown,
  hmacKey: string,
): FilteredPayload | null {
  if (!raw || typeof raw !== "object") return null;
  const src = raw as RawPayloadMaybe;

  const out: Partial<FilteredPayload> = {};
  for (const key of ALLOWED_FIELDS) {
    const v = src[key];
    if (typeof v === "string" && v.length > 0) {
      (out as Record<string, string>)[key] = v;
    }
  }

  for (const key of REQUIRED_FIELDS) {
    if (!out[key]) return null;
  }

  // Replace actor.oid with HMAC if present.
  const oidRaw = src.actor?.oid;
  if (typeof oidRaw === "string" && oidRaw.length > 0 && hmacKey) {
    out.actorHash = actorHash(hmacKey, out.tenantId as string, oidRaw);
  }

  return out as FilteredPayload;
}
