import * as grpc from "@grpc/grpc-js";
import * as protoLoader from "@grpc/proto-loader";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));

const PROTO_OPTIONS: protoLoader.Options = {
  keepCase: false,
  longs: String,
  enums: String,
  defaults: true,
  oneofs: true,
};

function loadProto(relativePath: string): grpc.GrpcObject {
  const protoPath = resolve(__dirname, "../../..", "proto", relativePath);
  const packageDef = protoLoader.loadSync(protoPath, PROTO_OPTIONS);
  return grpc.loadPackageDefinition(packageDef);
}

export type GrpcClients = {
  employee: grpc.Client;
  attendance: grpc.Client;
  organization: grpc.Client;
};

export function createGrpcClients(
  employeeUrl: string,
  attendanceUrl: string,
  organizationUrl: string,
): GrpcClients {
  const credentials = grpc.credentials.createInsecure();

  const empProto = loadProto("hrsystem/employee/v1/employee.proto");
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const EmployeeService = (empProto.hrsystem as any).employee.v1
    .EmployeeService;

  const attProto = loadProto("hrsystem/attendance/v1/attendance.proto");
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const AttendanceService = (attProto.hrsystem as any).attendance.v1
    .AttendanceService;

  const orgProto = loadProto("hrsystem/organization/v1/organization.proto");
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const OrganizationService = (orgProto.hrsystem as any).organization.v1
    .OrganizationService;

  return {
    employee: new EmployeeService(employeeUrl, credentials),
    attendance: new AttendanceService(attendanceUrl, credentials),
    organization: new OrganizationService(organizationUrl, credentials),
  };
}

/**
 * Wrap a gRPC unary call in a Promise.
 */
export function grpcUnary<TReq, TRes>(
  client: grpc.Client,
  method: string,
  request: TReq,
): Promise<TRes> {
  return new Promise((resolve, reject) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (client as any)[method](
      request,
      (error: grpc.ServiceError | null, response: TRes) => {
        if (error) {
          reject(error);
        } else {
          resolve(response);
        }
      },
    );
  });
}

/**
 * Convert gRPC status codes to HTTP status codes.
 */
export function grpcStatusToHttp(code: grpc.status): number {
  switch (code) {
    case grpc.status.OK:
      return 200;
    case grpc.status.NOT_FOUND:
      return 404;
    case grpc.status.ALREADY_EXISTS:
      return 409;
    case grpc.status.INVALID_ARGUMENT:
      return 400;
    case grpc.status.FAILED_PRECONDITION:
      return 409;
    case grpc.status.PERMISSION_DENIED:
      return 403;
    case grpc.status.UNAUTHENTICATED:
      return 401;
    case grpc.status.RESOURCE_EXHAUSTED:
      return 429;
    case grpc.status.UNAVAILABLE:
      return 503;
    case grpc.status.DEADLINE_EXCEEDED:
      return 504;
    default:
      return 500;
  }
}
