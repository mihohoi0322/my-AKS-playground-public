import { describe, it, expect } from "vitest";
import { filterPayload } from "../../../app/lib/realtime/payloadFilter.js";

const HMAC_KEY = "test-hmac-key-do-not-use-in-prod";

const VALID = {
  auditId: "audit-1",
  tenantId: "tenant-1",
  resourceType: "Employee",
  resourceId: "emp-1",
  eventType: "Updated",
  occurredAt: "2026-04-26T10:00:00Z",
  scopeKey: "org-1",
};

describe("payloadFilter", () => {
  it("keeps only allow-listed fields", () => {
    const filtered = filterPayload(
      {
        ...VALID,
        // forbidden fields below — must be stripped
        actor: { oid: "actor-oid-9", displayName: "Alice" },
        email: "alice@example.com",
        ipAddress: "10.0.0.1",
        userAgent: "curl/8",
        comment: "free-form sensitive note",
        salary: 1234567,
      },
      HMAC_KEY,
    );
    expect(filtered).not.toBeNull();
    const keys = Object.keys(filtered!).sort();
    // allow-list + actorHash (because actor.oid present)
    expect(keys).toEqual(
      [
        "actorHash",
        "auditId",
        "eventType",
        "occurredAt",
        "resourceId",
        "resourceType",
        "scopeKey",
        "tenantId",
      ].sort(),
    );
    expect((filtered as Record<string, unknown>).email).toBeUndefined();
    expect((filtered as Record<string, unknown>).comment).toBeUndefined();
  });

  it("returns null when a required field is missing", () => {
    const { auditId: _drop, ...rest } = VALID;
    void _drop;
    const filtered = filterPayload(rest, HMAC_KEY);
    expect(filtered).toBeNull();
  });

  it("does not produce actorHash when actor.oid is absent", () => {
    const filtered = filterPayload(VALID, HMAC_KEY);
    expect(filtered?.actorHash).toBeUndefined();
  });

  it("actorHash is tenant-scoped (different tenant → different hash)", () => {
    const a = filterPayload(
      { ...VALID, tenantId: "tenant-A", actor: { oid: "same-oid" } },
      HMAC_KEY,
    );
    const b = filterPayload(
      { ...VALID, tenantId: "tenant-B", actor: { oid: "same-oid" } },
      HMAC_KEY,
    );
    expect(a?.actorHash).toBeDefined();
    expect(b?.actorHash).toBeDefined();
    expect(a?.actorHash).not.toBe(b?.actorHash);
  });

  it("returns null on non-object input", () => {
    expect(filterPayload(null, HMAC_KEY)).toBeNull();
    expect(filterPayload("not-an-object", HMAC_KEY)).toBeNull();
  });
});
