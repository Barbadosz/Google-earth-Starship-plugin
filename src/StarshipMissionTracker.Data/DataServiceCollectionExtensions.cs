using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Data.Import;
using StarshipMissionTracker.Data.Repositories;

namespace StarshipMissionTracker.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the context factory, repositories, importer and initializer (all singletons: every call opens its own short-lived
    /// context, so hosted services can use them). The caller registers <see cref="IMissionLoader"/> and logging.
    /// </summary>
    public static IServiceCollection AddStarshipData(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return services.AddStarshipData(_ => connectionString);
    }

    /// <summary>
    /// As <see cref="AddStarshipData(IServiceCollection, string)"/>, but the connection string is resolved from the built
    /// service provider (so it can come from configuration that is final only after the host is built, e.g. in tests).
    /// </summary>
    public static IServiceCollection AddStarshipData(this IServiceCollection services, Func<IServiceProvider, string> connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        services.AddDbContextFactory<StarshipDbContext>((provider, options) => options.UseSqlite(connectionString(provider)));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMissionRepository, MissionRepository>();
        services.AddSingleton<ITelemetryRepository, TelemetryRepository>();
        services.AddSingleton<IMissionImporter, MissionImporter>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }
}
