import { Type, type Static } from "@sinclair/typebox";

export const MainResponseSchema = Type.Object({
  message: Type.String(),
  redis_data: Type.Optional(Type.Union([Type.String(), Type.Null()])),
  timestamp: Type.String(),
});
export type MainResponse = Static<typeof MainResponseSchema>;

export const HealthResponseSchema = Type.Object({
  status: Type.String(),
  redis: Type.Optional(
    Type.Object({
      connected: Type.Boolean(),
      latency_ms: Type.Number(),
    }),
  ),
  timestamp: Type.String(),
});
export type HealthResponse = Static<typeof HealthResponseSchema>;

export const ErrorResponseSchema = Type.Object({
  error: Type.String(),
  detail: Type.Optional(Type.Union([Type.String(), Type.Null()])),
  timestamp: Type.String(),
  request_id: Type.Optional(Type.Union([Type.String(), Type.Null()])),
});
export type ErrorResponse = Static<typeof ErrorResponseSchema>;
