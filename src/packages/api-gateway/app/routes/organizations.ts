import type { FastifyInstance } from "fastify";
import * as grpc from "@grpc/grpc-js";
import { grpcUnary, grpcStatusToHttp } from "../grpc-client.js";

export function registerOrganizationRoutes(app: FastifyInstance): void {
  const client = app.grpcClients.organization;

  // GET /api/organizations — list root-level organizations
  app.get<{
    Querystring: { limit?: string; cursor?: string };
  }>("/api/organizations", async (request, reply) => {
    try {
      const result = await grpcUnary(client, "listChildren", {
        orgId: "",
        limit: parseInt(request.query.limit ?? "50", 10),
        cursor: request.query.cursor ?? "",
      });
      return reply.send(result);
    } catch (err) {
      return handleGrpcError(reply, err);
    }
  });

  // POST /api/organizations
  app.post("/api/organizations", async (request, reply) => {
    try {
      const result = await grpcUnary(
        client,
        "createOrganization",
        request.body,
      );
      return reply.status(201).send(result);
    } catch (err) {
      return handleGrpcError(reply, err);
    }
  });

  // GET /api/organizations/:orgId
  app.get<{ Params: { orgId: string } }>(
    "/api/organizations/:orgId",
    async (request, reply) => {
      try {
        const result = await grpcUnary(client, "getOrganization", {
          orgId: request.params.orgId,
        });
        return reply.send(result);
      } catch (err) {
        return handleGrpcError(reply, err);
      }
    },
  );

  // PATCH /api/organizations/:orgId
  app.patch<{ Params: { orgId: string } }>(
    "/api/organizations/:orgId",
    async (request, reply) => {
      try {
        const result = await grpcUnary(client, "updateOrganization", {
          orgId: request.params.orgId,
          ...(request.body as object),
        });
        return reply.send(result);
      } catch (err) {
        return handleGrpcError(reply, err);
      }
    },
  );

  // GET /api/organizations/:orgId/children
  app.get<{
    Params: { orgId: string };
    Querystring: { limit?: string; cursor?: string };
  }>("/api/organizations/:orgId/children", async (request, reply) => {
    try {
      const result = await grpcUnary(client, "listChildren", {
        orgId: request.params.orgId,
        limit: parseInt(request.query.limit ?? "20", 10),
        cursor: request.query.cursor ?? "",
      });
      return reply.send(result);
    } catch (err) {
      return handleGrpcError(reply, err);
    }
  });

  // GET /api/organizations/:orgId/tree
  app.get<{ Params: { orgId: string } }>(
    "/api/organizations/:orgId/tree",
    async (request, reply) => {
      try {
        const result = await grpcUnary(client, "getOrganizationTree", {
          orgId: request.params.orgId,
        });
        return reply.send(result);
      } catch (err) {
        return handleGrpcError(reply, err);
      }
    },
  );

  // DELETE /api/organizations/:orgId — Phase 1a (Issue #26)
  // Returns 204 on success. Errors are returned in the structured form
  // `{ error: { code, message } }` per docs/api-spec.md §3.4.3.
  // NOTE: `details: { childCount, childOrgIds }` is intentionally not
  // populated yet — gRPC side currently surfaces those only via the message
  // string. Tracked as a follow-up issue.
  app.delete<{ Params: { orgId: string } }>(
    "/api/organizations/:orgId",
    async (request, reply) => {
      try {
        await grpcUnary(client, "deleteOrganization", {
          orgId: request.params.orgId,
        });
        return reply.code(204).send();
      } catch (err) {
        return handleDeleteOrgError(reply, err);
      }
    },
  );
}

/**
 * Map a gRPC error from `OrganizationService.DeleteOrganization` to the
 * structured HTTP error response defined in docs/api-spec.md §3.4.3.
 */
// TODO(#44): エラーレスポンス契約 { error: { code, message } } を全ルートに統一する。
// 現状 DELETE 組織のみ新形式、他は { error: <string> }。詳細は Issue #44。
function handleDeleteOrgError(
  reply: {
    status: (code: number) => { send: (body: unknown) => unknown };
  },
  err: unknown,
) {
  if (!(err instanceof Error) || !("code" in err)) {
    return reply.status(500).send({
      error: { code: "INTERNAL_ERROR", message: "Internal Server Error" },
    });
  }

  const grpcErr = err as grpc.ServiceError;
  const message = grpcErr.details || grpcErr.message;
  const httpStatus = grpcStatusToHttp(grpcErr.code);

  let code: string;
  switch (grpcErr.code) {
    case grpc.status.INVALID_ARGUMENT:
      code = "VALIDATION_ERROR";
      break;
    case grpc.status.NOT_FOUND:
      code = "ORG_NOT_FOUND";
      break;
    case grpc.status.FAILED_PRECONDITION:
      if (/child organization/i.test(message)) {
        code = "ORG_HAS_CHILDREN";
      } else if (/assigned employees/i.test(message)) {
        code = "ORG_HAS_EMPLOYEES";
      } else {
        code = "PRECONDITION_FAILED";
      }
      break;
    case grpc.status.UNAVAILABLE:
      code = "SERVICE_UNAVAILABLE";
      break;
    case grpc.status.INTERNAL:
      code = "INTERNAL_ERROR";
      break;
    default:
      code = "INTERNAL_ERROR";
      break;
  }

  return reply.status(httpStatus).send({ error: { code, message } });
}

function handleGrpcError(
  reply: { status: (code: number) => { send: (body: unknown) => unknown } },
  err: unknown,
) {
  if (err instanceof Error && "code" in err) {
    const grpcErr = err as grpc.ServiceError;
    const httpStatus = grpcStatusToHttp(grpcErr.code);
    return reply
      .status(httpStatus)
      .send({ error: grpcErr.details || grpcErr.message });
  }
  return reply.status(500).send({ error: "Internal Server Error" });
}
