import type { FastifyInstance } from "fastify";
import * as grpc from "@grpc/grpc-js";
import { grpcUnary, grpcStatusToHttp } from "../grpc-client.js";

export function registerEmployeeRoutes(app: FastifyInstance): void {
  const client = app.grpcClients.employee;

  // POST /api/employees
  app.post("/api/employees", async (request, reply) => {
    try {
      const result = await grpcUnary(client, "createEmployee", request.body);
      return reply.status(201).send(result);
    } catch (err) {
      return handleGrpcError(reply, err);
    }
  });

  // GET /api/employees/:employeeId
  app.get<{ Params: { employeeId: string } }>(
    "/api/employees/:employeeId",
    async (request, reply) => {
      try {
        const result = await grpcUnary(client, "getEmployee", {
          employeeId: request.params.employeeId,
        });
        return reply.send(result);
      } catch (err) {
        return handleGrpcError(reply, err);
      }
    },
  );

  // PATCH /api/employees/:employeeId
  app.patch<{ Params: { employeeId: string } }>(
    "/api/employees/:employeeId",
    async (request, reply) => {
      try {
        const result = await grpcUnary(client, "updateEmployee", {
          employeeId: request.params.employeeId,
          ...(request.body as object),
        });
        return reply.send(result);
      } catch (err) {
        return handleGrpcError(reply, err);
      }
    },
  );

  // DELETE /api/employees/:employeeId
  app.delete<{ Params: { employeeId: string } }>(
    "/api/employees/:employeeId",
    async (request, reply) => {
      try {
        const result = await grpcUnary(client, "deleteEmployee", {
          employeeId: request.params.employeeId,
        });
        return reply.send(result);
      } catch (err) {
        return handleGrpcError(reply, err);
      }
    },
  );

  // GET /api/employees
  app.get<{
    Querystring: {
      limit?: string;
      cursor?: string;
      status?: string;
      departmentId?: string;
    };
  }>("/api/employees", async (request, reply) => {
    try {
      const result = await grpcUnary(client, "listEmployees", {
        limit: parseInt(request.query.limit ?? "20", 10),
        cursor: request.query.cursor ?? "",
        status: request.query.status,
        departmentId: request.query.departmentId,
      });
      return reply.send(result);
    } catch (err) {
      return handleGrpcError(reply, err);
    }
  });
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
