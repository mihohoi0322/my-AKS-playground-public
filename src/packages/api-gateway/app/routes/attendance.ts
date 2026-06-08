import type { FastifyInstance } from "fastify";
import * as grpc from "@grpc/grpc-js";
import { grpcUnary, grpcStatusToHttp } from "../grpc-client.js";

export function registerAttendanceRoutes(app: FastifyInstance): void {
  const client = app.grpcClients.attendance;

  // POST /api/attendance/clock-in
  app.post("/api/attendance/clock-in", async (request, reply) => {
    try {
      const result = await grpcUnary(client, "clockIn", request.body);
      return reply.status(201).send(result);
    } catch (err) {
      return handleGrpcError(reply, err);
    }
  });

  // POST /api/attendance/clock-out
  app.post("/api/attendance/clock-out", async (request, reply) => {
    try {
      const result = await grpcUnary(client, "clockOut", request.body);
      return reply.send(result);
    } catch (err) {
      return handleGrpcError(reply, err);
    }
  });

  // GET /api/attendance/:attendanceId
  app.get<{ Params: { attendanceId: string } }>(
    "/api/attendance/:attendanceId",
    async (request, reply) => {
      try {
        const result = await grpcUnary(client, "getAttendance", {
          attendanceId: request.params.attendanceId,
        });
        return reply.send(result);
      } catch (err) {
        return handleGrpcError(reply, err);
      }
    },
  );

  // GET /api/attendance?employeeId=...&startDate=...&endDate=...
  app.get<{
    Querystring: {
      employeeId: string;
      startDate: string;
      endDate: string;
      limit?: string;
      cursor?: string;
    };
  }>("/api/attendance", async (request, reply) => {
    try {
      const result = await grpcUnary(client, "listAttendanceByPeriod", {
        employeeId: request.query.employeeId,
        startDate: request.query.startDate,
        endDate: request.query.endDate,
        limit: parseInt(request.query.limit ?? "20", 10),
        cursor: request.query.cursor ?? "",
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
