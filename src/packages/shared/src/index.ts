/**
 * @hrsystem/shared — common utilities for Node.js services.
 * Exports config helpers, telemetry setup, Redis client factory, and Cosmos DB client.
 */

export { PACKAGE_VERSION } from "./version.js";

export {
  getCosmosClient,
  getCosmosDatabase,
  getCosmosContainer,
  loadCosmosConfig,
  resetCosmosClient,
  type CosmosConfig,
} from "./cosmos-client.js";
