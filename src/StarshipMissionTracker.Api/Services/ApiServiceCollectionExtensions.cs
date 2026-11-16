using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Api.Options;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Kml.Dynamic;
using StarshipMissionTracker.Telemetry.Simulation;
using StarshipMissionTracker.Trajectory.Calculation;

namespace StarshipMissionTracker.Api.Services;

/// <summary>
/// Registers the Api building blocks (options with start-up validation, KML services, filters, gzip). Repositories,
/// the simulation controller, <see cref="TimeProvider"/> and logging are registered by the wiring (Program.cs).
/// </summary>
public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddStarshipApi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<NetworkLinkOptions>()
            .Bind(configuration.GetSection(NetworkLinkOptions.SectionName))
            .Validate(o => o.Validate() is null, "Invalid NetworkLinks options (refresh intervals must be > 0 and >= MinRefreshPeriodSeconds).")
            .ValidateOnStart();
        services.AddOptions<HistoryOptions>()
            .Bind(configuration.GetSection(HistoryOptions.SectionName))
            .Validate(o => o.Validate() is null, "Invalid History options (MaxPointsPerVehicle in [2, 100000]).")
            .ValidateOnStart();
        services.AddOptions<LiveOptions>()
            .Bind(configuration.GetSection(LiveOptions.SectionName))
            .Validate<IOptions<SimulationOptions>>(
                (o, simulation) => o.Validate(simulation.Value.SampleIntervalSeconds) is null,
                "Invalid Live options (StaleAfterSeconds must be > Simulation:SampleIntervalSeconds / minimum speed).")
            .ValidateOnStart();

        services.AddOptions<HostFilterOptions>()
            .Bind(configuration.GetSection(HostFilterOptions.SectionName))
            .Validate(o => o.Validate() is null, "Invalid HostFilter options (AllowedHostNames: plain host names, no wildcards).")
            .ValidateOnStart();

        services.AddSingleton<RevisionCounter>();
        services.AddSingleton<StaleTelemetryTracker>();
        services.AddSingleton(_ => new FlightPathPreparer());
        services.AddSingleton<PlannedDocumentCache>();
        services.TryAddSingleton<IDynamicKmlBuilder, DynamicKmlBuilder>();
        services.AddScoped<KmlDocumentService>();
        services.AddKmlResponseCompression();
        return services;
    }
}
