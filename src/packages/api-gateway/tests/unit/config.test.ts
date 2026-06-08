import { describe, it, expect, vi } from "vitest";
import { loadConfigFromEnv, toGrpcTarget } from "../../app/config.js";

describe("loadConfigFromEnv", () => {
  it("should return defaults when no env vars set", () => {
    const config = loadConfigFromEnv();
    expect(config.APP_PORT).toBe(8000);
    expect(config.LOG_LEVEL).toBe("info");
    expect(config.REDIS_ENABLED).toBe(false);
    expect(config.REDIS_SSL).toBe(true);
    expect(config.REDIS_PORT).toBe(10000);
    expect(config.TELEMETRY_ENABLED).toBe(false); // set by vitest.config
    expect(config.TELEMETRY_SAMPLING_RATE).toBe(0.1);
  });

  it("should read APP_PORT from env", () => {
    vi.stubEnv("APP_PORT", "3000");
    const config = loadConfigFromEnv();
    expect(config.APP_PORT).toBe(3000);
    vi.unstubAllEnvs();
  });

  it("should prefer AZURE_REDIS_HOST over REDIS_HOST", () => {
    vi.stubEnv("AZURE_REDIS_HOST", "azure-redis.example.com");
    const config = loadConfigFromEnv();
    expect(config.REDIS_HOST).toBe("azure-redis.example.com");
    vi.unstubAllEnvs();
  });

  it("should read REDIS_HOST when AZURE_REDIS_HOST not set", () => {
    vi.stubEnv("REDIS_HOST", "local-redis");
    const config = loadConfigFromEnv();
    expect(config.REDIS_HOST).toBe("local-redis");
    vi.unstubAllEnvs();
  });

  it("should parse boolean REDIS_ENABLED", () => {
    vi.stubEnv("REDIS_ENABLED", "true");
    const config = loadConfigFromEnv();
    expect(config.REDIS_ENABLED).toBe(true);
    vi.unstubAllEnvs();
  });
});

describe("toGrpcTarget", () => {
  it("returns host:port form unchanged (Docker / K8s service DNS)", () => {
    expect(toGrpcTarget("employee-service:50051")).toBe(
      "employee-service:50051",
    );
    expect(toGrpcTarget("localhost:50051")).toBe("localhost:50051");
  });

  it("strips http:// scheme (Aspire compatibility)", () => {
    expect(toGrpcTarget("http://localhost:5280")).toBe("localhost:5280");
    expect(toGrpcTarget("http://employee-service:50051")).toBe(
      "employee-service:50051",
    );
  });

  it("defaults http port to 80 and https port to 443 when omitted", () => {
    expect(toGrpcTarget("http://example.com")).toBe("example.com:80");
    expect(toGrpcTarget("https://example.com")).toBe("example.com:443");
  });

  it("keeps explicit https port", () => {
    expect(toGrpcTarget("https://example.com:8443")).toBe("example.com:8443");
  });

  it("returns raw value for unknown / malformed input", () => {
    expect(toGrpcTarget("not a url")).toBe("not a url");
    expect(toGrpcTarget("")).toBe("");
  });
});
