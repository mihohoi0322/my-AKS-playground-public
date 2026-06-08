import Redis, { type Redis as RedisType } from "ioredis";
import { DefaultAzureCredential } from "@azure/identity";
import { recordRedisMetrics } from "./telemetry.js";
import type { AppConfig } from "./config.js";

const REDIS_SCOPE = "https://redis.azure.com/.default";

/**
 * Azure Managed Redis client with Entra ID authentication.
 *
 * Uses DefaultAzureCredential for automatic token management.
 * AZURE_CLIENT_ID env var selects the User-Assigned Managed Identity.
 */
export class RedisClient {
  private client: RedisType | null = null;
  private credential: DefaultAzureCredential | null = null;
  private tokenRefreshTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly host: string;
  private readonly port: number;
  private readonly config: AppConfig;

  constructor(host: string, port: number, config: AppConfig) {
    this.host = host;
    this.port = port;
    this.config = config;
  }

  private async getToken(): Promise<{ username: string; password: string }> {
    if (!this.credential) {
      this.credential = new DefaultAzureCredential();
    }
    const token = await this.credential.getToken(REDIS_SCOPE);
    // Extract Object ID from JWT for Redis AUTH username
    const parts = token.token.split(".");
    const payload = JSON.parse(
      Buffer.from(parts[1], "base64url").toString("utf-8"),
    );
    const username = (payload.oid as string) ?? "";
    return { username, password: token.token };
  }

  private scheduleTokenRefresh(): void {
    // Refresh token every 20 minutes (tokens typically valid for 60-90 min)
    const refreshIntervalMs = 20 * 60 * 1000;
    this.tokenRefreshTimer = setTimeout(async () => {
      try {
        if (!this.client) return;
        const { username, password } = await this.getToken();
        await this.client.auth(username, password);
        this.scheduleTokenRefresh();
      } catch {
        // Retry in 1 minute on failure
        this.tokenRefreshTimer = setTimeout(
          () => this.scheduleTokenRefresh(),
          60_000,
        );
      }
    }, refreshIntervalMs);
  }

  async connect(): Promise<void> {
    const { username, password } = await this.getToken();

    this.client = new Redis.default({
      host: this.host,
      port: this.port,
      username,
      password,
      tls: this.config.REDIS_SSL ? {} : undefined,
      connectTimeout: this.config.REDIS_SOCKET_CONNECT_TIMEOUT,
      commandTimeout: this.config.REDIS_SOCKET_TIMEOUT,
      maxRetriesPerRequest: this.config.REDIS_MAX_RETRIES,
      lazyConnect: true,
      enableReadyCheck: true,
    });

    await this.client.connect();
    await this.client.ping();
    this.scheduleTokenRefresh();
  }

  async close(): Promise<void> {
    if (this.tokenRefreshTimer) {
      clearTimeout(this.tokenRefreshTimer);
      this.tokenRefreshTimer = null;
    }
    if (this.client) {
      await this.client.quit();
      this.client = null;
    }
    this.credential = null;
  }

  async get(key: string): Promise<string | null> {
    if (!this.client) throw new Error("Redis client is not connected");
    return this.client.get(key);
  }

  async set(key: string, value: string): Promise<void> {
    if (!this.client) throw new Error("Redis client is not connected");
    await this.client.set(key, value);
  }

  async increment(key: string): Promise<number> {
    if (!this.client) throw new Error("Redis client is not connected");
    return this.client.incr(key);
  }

  /**
   * Expose the underlying ioredis client for advanced operations (XADD, XREAD,
   * EVAL, duplicate()). Returns null if not connected.
   */
  getRawClient(): RedisType | null {
    return this.client;
  }

  async ping(): Promise<boolean> {
    if (!this.client) throw new Error("Redis client is not connected");
    const start = performance.now();
    try {
      await this.client.ping();
      const latencyMs = Math.round(performance.now() - start);
      recordRedisMetrics(true, latencyMs);
      return true;
    } catch {
      recordRedisMetrics(false, -1);
      throw new Error("Redis ping failed");
    }
  }
}
