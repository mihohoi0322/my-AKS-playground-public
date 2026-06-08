using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace HRSystem.Shared.Cosmos;

public interface ICosmosClientFactory
{
    CosmosClient CreateClient();
}

public sealed class CosmosClientFactory : ICosmosClientFactory, IDisposable
{
    private readonly CosmosSettings _settings;
    private readonly ILogger<CosmosClientFactory> _logger;
    private CosmosClient? _client;
    private readonly object _lock = new();

    public CosmosClientFactory(CosmosSettings settings, ILogger<CosmosClientFactory> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    public CosmosClient CreateClient()
    {
        if (_client is not null) return _client;

        lock (_lock)
        {
            if (_client is not null) return _client;

            var options = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                },
                ConnectionMode = ConnectionMode.Direct,
                ApplicationName = "hrsystem-lab"
            };

            // Prefer connection string (from Aspire), fall back to endpoint + DefaultAzureCredential
            if (!string.IsNullOrEmpty(_settings.ConnectionString))
            {
                _logger.LogInformation("Creating Cosmos DB client from connection string, database {Database}", _settings.DatabaseName);
                // Emulator uses Gateway mode
                options.ConnectionMode = ConnectionMode.Gateway;
                _client = new CosmosClient(_settings.ConnectionString, options);
            }
            else if (!string.IsNullOrEmpty(_settings.Endpoint))
            {
                _logger.LogInformation("Creating Cosmos DB client for {Endpoint}, database {Database}", _settings.Endpoint, _settings.DatabaseName);
                _client = new CosmosClient(_settings.Endpoint, new DefaultAzureCredential(), options);
            }
            else
            {
                _logger.LogWarning("No Cosmos DB endpoint or connection string configured. Client creation deferred.");
                throw new InvalidOperationException("Cosmos DB is not configured. Set COSMOS_ENDPOINT or provide a connection string.");
            }

            return _client;
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
