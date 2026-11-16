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
        services.AddDbContextFactory<StarshipDbContext>(options => options.UseSqlite(connectionString));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMissionRepository, MissionRepository>();
        services.AddSingleton<ITelemetryRepository, TelemetryRepository>();
        services.AddSingleton<IMissionImporter, MissionImporter>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }
}
