using HRSystem.Shared.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HRSystem.Shared.Audit.Outbox;

/// <summary>
/// Registration helpers for <see cref="AuditOutboxWorker"/>.
///
/// <para>
/// The worker is intentionally <em>not</em> registered by
/// <see cref="ServiceCollectionExtensions.AddHRSystemShared"/>: AppHost and the existing
/// per-service Program.cs files do not run the outbox today. W3 will wire it into each
/// business service explicitly.
/// </para>
/// </summary>
public static class AuditOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Register the outbox <see cref="AuditOutboxWorker"/>, its options and dependencies.
    /// Binds <see cref="AuditOutboxOptions"/> from the <c>AuditOutbox</c> configuration
    /// section and applies an optional <paramref name="configure"/> override.
    /// </summary>
    public static IHostApplicationBuilder AddAuditOutboxWorker(
        this IHostApplicationBuilder builder,
        Action<AuditOutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddAuditOutboxWorker(builder.Configuration, configure);
        return builder;
    }

    /// <summary>
    /// Lower-level overload that operates on raw <see cref="IServiceCollection"/>. Used by the
    /// <see cref="IHostApplicationBuilder"/> extension and by tests that don't need a host.
    /// </summary>
    public static IServiceCollection AddAuditOutboxWorker(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AuditOutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var optionsBuilder = services
            .AddOptions<AuditOutboxOptions>()
            .Bind(configuration.GetSection(AuditOutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        // CosmosSettings: reuse the shared settings if it's already registered (the typical
        // case when AddHRSystemShared was called first); otherwise bind a fresh instance from
        // the Cosmos:* config section so the worker can be registered standalone.
        services.TryAddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var settings = new CosmosSettings();
            cfg.GetSection("Cosmos").Bind(settings);
            settings.Endpoint = cfg["COSMOS_ENDPOINT"] ?? settings.Endpoint;
            settings.DatabaseName = cfg["COSMOS_DATABASE"] ?? settings.DatabaseName;
            return settings;
        });

        services.TryAddSingleton<IAuditOutboxCosmosClientProvider, DefaultAuditOutboxCosmosClientProvider>();
        services.TryAddSingleton<IAuditWriter, NoopAuditWriter>();
        services.AddHostedService<AuditOutboxWorker>();
        return services;
    }
}
