using HRSystem.Shared.Audit;
using HRSystem.Shared.Audit.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HRSystem.Shared.Tests.Audit.Outbox;

/// <summary>
/// Wiring tests for <see cref="AuditOutboxServiceCollectionExtensions"/>. These tests do not
/// connect to Cosmos: they only verify that DI registers the worker, options and ancillary
/// services correctly. End-to-end integration with Cosmos is exercised in W3.
/// </summary>
public sealed class AuditOutboxServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?>? overrides = null)
    {
        var data = new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://example.documents.azure.com:443/",
            ["Cosmos:DatabaseName"] = "hrsystem",
        };
        if (overrides is not null)
        {
            foreach (var kv in overrides) data[kv.Key] = kv.Value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [Fact]
    public void AddAuditOutboxWorker_RegistersHostedService_AndOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(BuildConfig());
        services.AddAuditOutboxWorker(BuildConfig());

        using var sp = services.BuildServiceProvider();

        // Hosted service registration: AuditOutboxWorker must be enumerable as IHostedService.
        var hosted = sp.GetServices<IHostedService>().ToArray();
        Assert.Contains(hosted, h => h is AuditOutboxWorker);

        // Options bind + defaults
        var options = sp.GetRequiredService<IOptions<AuditOutboxOptions>>().Value;
        Assert.Equal("auditHotIndex", options.SourceContainerName);
        Assert.Equal("auditLease", options.LeaseContainerName);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(17), options.LeaseRenewInterval);
        Assert.Equal(Environment.MachineName, options.InstanceName);
        Assert.Equal("audit-outbox", options.ProcessorName);
        Assert.Equal(100, options.MaxItemsPerBatch);
        Assert.True(options.Enabled);
    }

    [Fact]
    public void AddAuditOutboxWorker_OnHostBuilder_RegistersHostedService()
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://example.documents.azure.com:443/",
            ["Cosmos:DatabaseName"] = "hrsystem",
        });
        builder.Services.AddLogging();
        var returned = builder.AddAuditOutboxWorker();
        Assert.Same(builder, returned);

        using var host = builder.Build();
        var hosted = host.Services.GetServices<IHostedService>().ToArray();
        Assert.Contains(hosted, h => h is AuditOutboxWorker);
    }

    [Fact]
    public void AddAuditOutboxWorker_AppliesConfigureCallback()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(BuildConfig());
        services.AddAuditOutboxWorker(BuildConfig(), o =>
        {
            o.SourceContainerName = "altIndex";
            o.PollingInterval = TimeSpan.FromSeconds(2);
            o.MaxItemsPerBatch = 25;
        });

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<AuditOutboxOptions>>().Value;
        Assert.Equal("altIndex", options.SourceContainerName);
        Assert.Equal(TimeSpan.FromSeconds(2), options.PollingInterval);
        Assert.Equal(25, options.MaxItemsPerBatch);
        Assert.Equal("auditLease", options.LeaseContainerName); // unchanged default
    }

    [Fact]
    public void AddAuditOutboxWorker_ConfigBindingFromSection_Wins()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            ["AuditOutbox:SourceContainerName"] = "fromConfigIndex",
            ["AuditOutbox:LeaseContainerName"] = "fromConfigLease",
            ["AuditOutbox:PollingInterval"] = "00:00:03",
            ["AuditOutbox:Enabled"] = "false",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddAuditOutboxWorker(cfg);

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<AuditOutboxOptions>>().Value;
        Assert.Equal("fromConfigIndex", options.SourceContainerName);
        Assert.Equal("fromConfigLease", options.LeaseContainerName);
        Assert.Equal(TimeSpan.FromSeconds(3), options.PollingInterval);
        Assert.False(options.Enabled);
    }

    [Fact]
    public void AddAuditOutboxWorker_RegistersDefaultClientProvider_AndAuditWriter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(BuildConfig());
        services.AddLogging();
        services.AddAuditOutboxWorker(BuildConfig());

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IAuditOutboxCosmosClientProvider>();
        Assert.IsType<DefaultAuditOutboxCosmosClientProvider>(provider);

        var writer = sp.GetRequiredService<IAuditWriter>();
        Assert.IsType<NoopAuditWriter>(writer);
    }

    [Fact]
    public void AddAuditOutboxWorker_UsesPreRegisteredAuditWriter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(BuildConfig());
        var custom = new NoopAuditWriter();
        services.AddSingleton<IAuditWriter>(custom);
        services.AddAuditOutboxWorker(BuildConfig());

        using var sp = services.BuildServiceProvider();
        var writer = sp.GetRequiredService<IAuditWriter>();
        Assert.Same(custom, writer);
    }
}
