import { setupTelemetry } from "./telemetry.js";

// Initialize telemetry before importing anything else
setupTelemetry();

import { buildServer } from "./server.js";
import { loadConfigFromEnv } from "./config.js";

async function main(): Promise<void> {
  const config = loadConfigFromEnv();
  const app = await buildServer();

  try {
    await app.listen({ port: config.APP_PORT, host: "0.0.0.0" });
  } catch (err) {
    app.log.error(err);
    process.exit(1);
  }

  // Graceful shutdown
  const signals = ["SIGINT", "SIGTERM"] as const;
  for (const signal of signals) {
    process.on(signal, async () => {
      app.log.info(`Received ${signal}, shutting down gracefully...`);
      await app.close();
      process.exit(0);
    });
  }
}

main();
