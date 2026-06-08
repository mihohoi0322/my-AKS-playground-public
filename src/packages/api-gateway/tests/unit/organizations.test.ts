import { describe, it, expect, afterEach, vi } from "vitest";
import Fastify from "fastify";
import type { FastifyRequest } from "fastify";
import * as grpc from "@grpc/grpc-js";
import { registerOrganizationRoutes } from "../../app/routes/organizations.js";

vi.mock("../../app/telemetry.js", () => ({
  recordRedisMetrics: vi.fn(),
  recordSpanError: vi.fn(),
  setupTelemetry: vi.fn(),
}));

type GrpcCallback = (
  err: grpc.ServiceError | null,
  res?: Record<string, unknown>,
) => void;

/**
 * Build a fake organization gRPC client whose `deleteOrganization` invokes
 * the supplied callback handler. The callback signature mirrors
 * `@grpc/grpc-js` unary methods: `(request, callback) => void`.
 */
function buildFakeOrgClient(
  deleteHandler: (
    req: { orgId: string },
    cb: GrpcCallback,
  ) => void,
): Record<string, unknown> {
  return {
    deleteOrganization: deleteHandler,
    // Stubs for the other methods so registerOrganizationRoutes can decorate
    // freely; they are not exercised by the DELETE-focused tests below.
    listChildren: (_req: unknown, cb: GrpcCallback) => cb(null, {}),
    createOrganization: (_req: unknown, cb: GrpcCallback) => cb(null, {}),
    getOrganization: (_req: unknown, cb: GrpcCallback) => cb(null, {}),
    updateOrganization: (_req: unknown, cb: GrpcCallback) => cb(null, {}),
    getOrganizationTree: (_req: unknown, cb: GrpcCallback) => cb(null, {}),
  };
}

function makeGrpcError(code: grpc.status, message: string): grpc.ServiceError {
  const err = new Error(message) as grpc.ServiceError;
  err.code = code;
  err.details = message;
  err.metadata = new grpc.Metadata();
  return err;
}

describe("DELETE /api/organizations/:orgId", () => {
  let app: ReturnType<typeof Fastify>;
  const ORG_ID = "11111111-2222-3333-4444-555555555555";

  function setupApp(orgClient: Record<string, unknown>) {
    app = Fastify({ logger: false });
    app.decorate("grpcClients", {
      employee: {},
      attendance: {},
      organization: orgClient,
    });
    app.decorate("requestId", "");
    app.addHook("onRequest", async (request: FastifyRequest) => {
      request.requestId = "test-request-id";
    });
    registerOrganizationRoutes(app);
  }

  afterEach(async () => {
    await app.close();
  });

  it("returns 204 with empty body on successful delete", async () => {
    const deleteHandler = vi.fn(
      (_req: { orgId: string }, cb: GrpcCallback) => {
        cb(null, {});
      },
    );
    setupApp(buildFakeOrgClient(deleteHandler));
    await app.ready();

    const response = await app.inject({
      method: "DELETE",
      url: `/api/organizations/${ORG_ID}`,
    });

    expect(response.statusCode).toBe(204);
    expect(response.body).toBe("");
    expect(deleteHandler).toHaveBeenCalledWith(
      { orgId: ORG_ID },
      expect.any(Function),
    );
  });

  it("returns 400 VALIDATION_ERROR when gRPC returns INVALID_ARGUMENT", async () => {
    setupApp(
      buildFakeOrgClient((_req, cb) => {
        cb(makeGrpcError(grpc.status.INVALID_ARGUMENT, "orgId is empty"));
      }),
    );
    await app.ready();

    const response = await app.inject({
      method: "DELETE",
      url: `/api/organizations/${ORG_ID}`,
    });

    expect(response.statusCode).toBe(400);
    expect(response.json()).toEqual({
      error: { code: "VALIDATION_ERROR", message: "orgId is empty" },
    });
  });

  it("returns 404 ORG_NOT_FOUND when gRPC returns NOT_FOUND", async () => {
    setupApp(
      buildFakeOrgClient((_req, cb) => {
        cb(makeGrpcError(grpc.status.NOT_FOUND, "organization not found"));
      }),
    );
    await app.ready();

    const response = await app.inject({
      method: "DELETE",
      url: `/api/organizations/${ORG_ID}`,
    });

    expect(response.statusCode).toBe(404);
    expect(response.json()).toEqual({
      error: { code: "ORG_NOT_FOUND", message: "organization not found" },
    });
  });

  it("returns 409 ORG_HAS_CHILDREN when FAILED_PRECONDITION mentions child organization", async () => {
    setupApp(
      buildFakeOrgClient((_req, cb) => {
        cb(
          makeGrpcError(
            grpc.status.FAILED_PRECONDITION,
            "Organization 'abc' has 2 child organization(s) and cannot be deleted.",
          ),
        );
      }),
    );
    await app.ready();

    const response = await app.inject({
      method: "DELETE",
      url: `/api/organizations/${ORG_ID}`,
    });

    expect(response.statusCode).toBe(409);
    const body = response.json();
    expect(body.error.code).toBe("ORG_HAS_CHILDREN");
    expect(body.error.message).toContain("child organization");
  });

  it("returns 409 ORG_HAS_EMPLOYEES when FAILED_PRECONDITION mentions assigned employees", async () => {
    setupApp(
      buildFakeOrgClient((_req, cb) => {
        cb(
          makeGrpcError(
            grpc.status.FAILED_PRECONDITION,
            "Organization 'abc' still has assigned employees and cannot be deleted.",
          ),
        );
      }),
    );
    await app.ready();

    const response = await app.inject({
      method: "DELETE",
      url: `/api/organizations/${ORG_ID}`,
    });

    expect(response.statusCode).toBe(409);
    const body = response.json();
    expect(body.error.code).toBe("ORG_HAS_EMPLOYEES");
    expect(body.error.message).toContain("assigned employees");
  });

  it("returns 503 SERVICE_UNAVAILABLE when gRPC returns UNAVAILABLE", async () => {
    setupApp(
      buildFakeOrgClient((_req, cb) => {
        cb(
          makeGrpcError(
            grpc.status.UNAVAILABLE,
            "EmployeeService unreachable",
          ),
        );
      }),
    );
    await app.ready();

    const response = await app.inject({
      method: "DELETE",
      url: `/api/organizations/${ORG_ID}`,
    });

    expect(response.statusCode).toBe(503);
    expect(response.json()).toEqual({
      error: {
        code: "SERVICE_UNAVAILABLE",
        message: "EmployeeService unreachable",
      },
    });
  });

  it("returns 500 INTERNAL_ERROR when gRPC returns INTERNAL", async () => {
    setupApp(
      buildFakeOrgClient((_req, cb) => {
        cb(makeGrpcError(grpc.status.INTERNAL, "boom"));
      }),
    );
    await app.ready();

    const response = await app.inject({
      method: "DELETE",
      url: `/api/organizations/${ORG_ID}`,
    });

    expect(response.statusCode).toBe(500);
    expect(response.json()).toEqual({
      error: { code: "INTERNAL_ERROR", message: "boom" },
    });
  });
});
