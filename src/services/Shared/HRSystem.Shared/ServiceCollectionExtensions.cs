using HRSystem.Shared.Cosmos;
using HRSystem.Shared.Redis;
using HRSystem.Shared.Grpc;
using HRSystem.Shared.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace HRSystem.Shared;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHRSystemShared(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        // Cosmos DB — supports both Aspire (ConnectionStrings__cosmosdb) and env vars (COSMOS_ENDPOINT)
        var cosmosSettings = new CosmosSettings();
        configuration.GetSection("Cosmos").Bind(cosmosSettings);

        var cosmosConnectionString = configuration.GetConnectionString("cosmosdb");
        if (!string.IsNullOrEmpty(cosmosConnectionString))
        {
            cosmosSettings.ConnectionString = cosmosConnectionString;
            // Extract endpoint from connection string: "AccountEndpoint=https://...;AccountKey=..."
            var parts = cosmosConnectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.StartsWith("AccountEndpoint=", StringComparison.OrdinalIgnoreCase))
                    cosmosSettings.Endpoint = part["AccountEndpoint=".Length..];
            }
        }
        cosmosSettings.Endpoint = configuration["COSMOS_ENDPOINT"] ?? cosmosSettings.Endpoint;
        cosmosSettings.DatabaseName = configuration["COSMOS_DATABASE"] ?? cosmosSettings.DatabaseName;

        services.AddSingleton(cosmosSettings);
        services.AddSingleton<ICosmosClientFactory>(sp =>
            new CosmosClientFactory(
                cosmosSettings,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CosmosClientFactory>>()));

        // Redis — supports both Aspire (ConnectionStrings__redis) and env vars (REDIS_HOST)
        var redisSettings = new RedisSettings();
        configuration.GetSection("Redis").Bind(redisSettings);

        var redisConnectionString = configuration.GetConnectionString("redis");
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            redisSettings.ConnectionString = redisConnectionString;
            // Parse "host:port" format from Aspire
            var hostPort = redisConnectionString.Split(',')[0]; // take first entry
            var colonIdx = hostPort.LastIndexOf(':');
            if (colonIdx > 0)
            {
                redisSettings.Host = hostPort[..colonIdx];
                if (int.TryParse(hostPort[(colonIdx + 1)..], out var p)) redisSettings.Port = p;
            }
            else
            {
                redisSettings.Host = hostPort;
            }
            redisSettings.UseSsl = false;
            redisSettings.UseEntraIdAuth = false;
        }
        redisSettings.Host = configuration["REDIS_HOST"] ?? redisSettings.Host;
        if (int.TryParse(configuration["REDIS_PORT"], out var port)) redisSettings.Port = port;
        if (bool.TryParse(configuration["REDIS_ENABLED"], out var enabled)) redisSettings.Enabled = enabled;

        services.AddSingleton(redisSettings);
        services.AddSingleton<IRedisConnectionFactory, RedisConnectionFactory>();

        // gRPC interceptors
        services.AddSingleton<LoggingInterceptor>();
        services.AddSingleton<ValidationInterceptor>();

        // OpenTelemetry
        services.AddHRSystemTelemetry(serviceName);

        return services;
    }
}
