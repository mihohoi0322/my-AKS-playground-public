using Microsoft.Azure.Cosmos;

namespace HRSystem.Shared.Audit.Outbox;

/// <summary>
/// Provides the <see cref="CosmosClient"/> used by <see cref="AuditOutboxWorker"/>.
///
/// The outbox worker requires a CosmosClient configured with the System.Text.Json
/// serializer (per W2-B contract) so that <see cref="AuditOutboxDocument"/>'s
/// <c>JsonPropertyName</c> attributes are honoured for the system properties
/// <c>_etag</c> and <c>_ts</c>. The shared <c>ICosmosClientFactory</c> is intentionally
/// not reused: the workload pods that own audit data must not share Cosmos client
/// configuration with general-purpose repositories (ADR-007 amendment).
/// </summary>
public interface IAuditOutboxCosmosClientProvider
{
    /// <summary>Database name configured via <c>Cosmos:DatabaseName</c>.</summary>
    string DatabaseName { get; }

    /// <summary>
    /// Build (or return cached) <see cref="CosmosClient"/>. Implementations MUST be safe to
    /// call from <c>ExecuteAsync</c> on the BackgroundService.
    /// </summary>
    CosmosClient GetClient();
}
