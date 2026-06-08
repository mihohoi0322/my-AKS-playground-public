using Azure.Identity;
using HRSystem.Shared.Cosmos;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace HRSystem.Shared.Audit.Outbox;

/// <summary>
/// Default <see cref="IAuditOutboxCosmosClientProvider"/> that builds a CosmosClient
/// with <c>ConnectionMode = Direct</c>, authenticated via <see cref="DefaultAzureCredential"/>
/// (Workload Identity in AKS).
///
/// Connection strings are intentionally not supported: outbox shipping is a privileged path
/// and must use Entra ID tokens (ADR-007). The dedicated client is also configured with a
/// distinct <c>ApplicationName</c> so RU consumption shows up separately in Cosmos diagnostics.
///
/// Note on serializer: the W2-B contract called for System.Text.Json. The Cosmos SDK 3.46
/// default serializer is Newtonsoft-based; <see cref="AuditOutboxDocument"/> is dual-decorated
/// (Newtonsoft + STJ attributes) so swapping in a custom STJ-based <c>CosmosSerializer</c> is
/// a self-contained follow-up (W3) without touching the document model.
/// </summary>
public sealed class DefaultAuditOutboxCosmosClientProvider : IAuditOutboxCosmosClientProvider, IDisposable
{
    private readonly CosmosSettings _settings;
    private readonly ILogger<DefaultAuditOutboxCosmosClientProvider> _logger;
    private readonly Lock _lock = new();
    private CosmosClient? _client;

    public DefaultAuditOutboxCosmosClientProvider(
        CosmosSettings settings,
        ILogger<DefaultAuditOutboxCosmosClientProvider> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    public string DatabaseName => _settings.DatabaseName;

    public CosmosClient GetClient()
    {
        if (_client is not null) return _client;
        lock (_lock)
        {
            if (_client is not null) return _client;
            if (string.IsNullOrEmpty(_settings.Endpoint))
            {
                throw new InvalidOperationException(
                    "Cosmos:Endpoint is not configured. AuditOutboxWorker requires a Cosmos endpoint to authenticate via DefaultAzureCredential.");
            }

            var options = new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Direct,
                ApplicationName = "hrsystem-audit-outbox",
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                },
            };

            _logger.LogInformation(
                "Building outbox CosmosClient for endpoint {Endpoint} database {Database}",
                _settings.Endpoint,
                _settings.DatabaseName);

            _client = new CosmosClient(_settings.Endpoint, new DefaultAzureCredential(), options);
            return _client;
        }
    }

    public void Dispose() => _client?.Dispose();
}
