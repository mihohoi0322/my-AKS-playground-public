using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HRSystem.Shared.Redis;

public interface IRedisConnectionFactory
{
    Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class RedisConnectionFactory : IRedisConnectionFactory, IAsyncDisposable
{
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisConnectionFactory> _logger;
    private IConnectionMultiplexer? _connection;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public RedisConnectionFactory(RedisSettings settings, ILogger<RedisConnectionFactory> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    public async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsConnected: true }) return _connection;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsConnected: true }) return _connection;

            ConfigurationOptions options;

            // Prefer connection string (from Aspire), fall back to host:port
            if (!string.IsNullOrEmpty(_settings.ConnectionString))
            {
                _logger.LogInformation("Connecting to Redis via connection string");
                options = ConfigurationOptions.Parse(_settings.ConnectionString);
            }
            else
            {
                _logger.LogInformation("Connecting to Redis at {Host}:{Port}", _settings.Host, _settings.Port);
                options = ConfigurationOptions.Parse($"{_settings.Host}:{_settings.Port}");
                options.Ssl = _settings.UseSsl;

                if (_settings.UseEntraIdAuth)
                {
                    var credential = new DefaultAzureCredential();
                    await options.ConfigureForAzureWithTokenCredentialAsync(credential);
                }
            }

            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;

            _connection = await ConnectionMultiplexer.ConnectAsync(options);
            _logger.LogInformation("Successfully connected to Redis");
            return _connection;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
        _semaphore.Dispose();
    }
}
