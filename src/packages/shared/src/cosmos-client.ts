import { CosmosClient, type Database, type Container } from "@azure/cosmos";
import { DefaultAzureCredential } from "@azure/identity";

export interface CosmosConfig {
  /** Cosmos DB endpoint URL (e.g. https://xxx.documents.azure.com:443/) */
  endpoint: string;
  /** Database name */
  database: string;
  /** Optional: use connection key instead of Entra ID */
  key?: string;
}

let clientInstance: CosmosClient | null = null;

/**
 * Get or create a singleton CosmosClient.
 * Uses Entra ID (DefaultAzureCredential) by default.
 * Falls back to key-based auth if `config.key` is provided.
 */
export function getCosmosClient(config: CosmosConfig): CosmosClient {
  if (clientInstance) return clientInstance;

  if (config.key) {
    clientInstance = new CosmosClient({
      endpoint: config.endpoint,
      key: config.key,
    });
  } else {
    clientInstance = new CosmosClient({
      endpoint: config.endpoint,
      aadCredentials: new DefaultAzureCredential(),
    });
  }

  return clientInstance;
}

/**
 * Get a Cosmos DB Database reference.
 */
export function getCosmosDatabase(config: CosmosConfig): Database {
  return getCosmosClient(config).database(config.database);
}

/**
 * Get a Cosmos DB Container reference.
 */
export function getCosmosContainer(
  config: CosmosConfig,
  containerName: string,
): Container {
  return getCosmosDatabase(config).container(containerName);
}

/**
 * Load CosmosConfig from environment variables.
 */
export function loadCosmosConfig(): CosmosConfig {
  const endpoint = process.env.COSMOS_ENDPOINT;
  if (!endpoint) {
    throw new Error("COSMOS_ENDPOINT environment variable is required");
  }

  return {
    endpoint,
    database: process.env.COSMOS_DATABASE ?? "hrsystem",
    key: process.env.COSMOS_KEY,
  };
}

/**
 * Reset the singleton client (for testing).
 */
export function resetCosmosClient(): void {
  clientInstance = null;
}
