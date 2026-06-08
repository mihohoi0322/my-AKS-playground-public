import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "./api";

const originalFetch = global.fetch;

function mockFetch(response: Response) {
  const fn = vi.fn().mockResolvedValue(response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

describe("apiFetch", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it("does not set Content-Type when body is omitted", async () => {
    const fn = mockFetch(
      new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    await apiFetch<{ ok: boolean }>("/api/employees", { method: "DELETE" });

    expect(fn).toHaveBeenCalledTimes(1);
    const init = fn.mock.calls[0][1] as RequestInit;
    const headers = init.headers as Record<string, string>;
    expect(headers["Content-Type"]).toBeUndefined();
  });

  it("sets Content-Type: application/json when body is provided", async () => {
    const fn = mockFetch(
      new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    await apiFetch<{ ok: boolean }>("/api/employees", {
      method: "POST",
      body: JSON.stringify({ name: "x" }),
    });

    const init = fn.mock.calls[0][1] as RequestInit;
    const headers = init.headers as Record<string, string>;
    expect(headers["Content-Type"]).toBe("application/json");
  });

  it("returns undefined for 204 No Content without throwing", async () => {
    mockFetch(new Response(null, { status: 204 }));

    const result = await apiFetch<void>("/api/employees/123", {
      method: "DELETE",
    });

    expect(result).toBeUndefined();
  });

  it("throws with the server-provided error message on 4xx/5xx", async () => {
    mockFetch(
      new Response(JSON.stringify({ error: "boom" }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      }),
    );

    await expect(
      apiFetch("/api/employees", { method: "GET" }),
    ).rejects.toThrow("boom");
  });

  it("preserves user-provided headers", async () => {
    const fn = mockFetch(
      new Response(JSON.stringify({ ok: true }), { status: 200 }),
    );

    await apiFetch("/api/employees", {
      method: "GET",
      headers: { "X-Trace": "abc" },
    });

    const init = fn.mock.calls[0][1] as RequestInit;
    const headers = init.headers as Record<string, string>;
    expect(headers["X-Trace"]).toBe("abc");
  });
});
