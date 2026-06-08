import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    globals: true,
    environment: "node",
    include: ["tests/**/*.test.ts"],
    coverage: {
      provider: "v8",
      include: ["app/**/*.ts"],
      exclude: ["app/**/*.d.ts", "tests/**"],
      thresholds: {
        statements: 70,
        branches: 70,
        functions: 70,
        lines: 70,
      },
    },
    env: {
      TELEMETRY_ENABLED: "false",
      REDIS_ENABLED: "false",
    },
  },
});
