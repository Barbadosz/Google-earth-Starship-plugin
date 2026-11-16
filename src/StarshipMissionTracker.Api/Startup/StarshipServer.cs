using System.Globalization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using StarshipMissionTracker.Api.Endpoints;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Api.Options;
using StarshipMissionTracker.Api.Services;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Data;
using StarshipMissionTracker.Telemetry.Simulation;
using StarshipMissionTracker.Trajectory.Loading;

namespace StarshipMissionTracker.Api.Startup;

/// <summary>
/// The server wiring (N-30), split out of Program.cs so it stays readable. Registration reads configuration lazily
/// (options binding, factories), because a test host adds its configuration only when the host is built.
/// </summary>
public static class StarshipServer
{
    /// <summary>MIME type of the COLLADA models under <c>/assets/models</c> (not in the default provider).</summary>
    public const string ColladaMediaType = "model/vnd.collada+xml";

    /// <summary>Registers logging, paths, Data, the simulator (controller + hosted services, in start order) and the Api services.</summary>
    public static IServiceCollection AddStarshipServer(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PathOptions>().Bind(configuration.GetSection(PathOptions.SectionName));
        services.AddSingleton(provider => StarshipPaths.Resolve(
            provider.GetRequiredService<IOptions<PathOptions>>().Value,
            provider.GetRequiredService<IHostEnvironment>().ContentRootPath,
            AppContext.BaseDirectory));

        services.AddSerilog((provider, logger) => ConfigureLogger(logger, configuration, provider.GetRequiredService<StarshipPaths>()));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMissionLoader, MissionFileLoader>();
        services.AddStarshipData(provider => provider.GetRequiredService<StarshipPaths>().ConnectionString);

        services.AddOptions<SimulationOptions>()
            .Bind(configuration.GetSection(SimulationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SimulationOptions>, SimulationOptionsRules>();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<SimulationOptions>>().Value);
        services.AddSingleton<SimulationController>();
        services.AddSingleton<ISimulationControl>(provider => provider.GetRequiredService<SimulationController>());

        // Hosted services start one after another in this order: migration + import complete before the simulator
        // (and its optional auto-start) runs; Kestrel starts after all of them.
        services.AddSingleton<DatabaseStartupService>();
        services.AddHostedService(provider => provider.GetRequiredService<DatabaseStartupService>());
        services.AddHostedService<SimulationHostedService>();

        services.AddStarshipApi(configuration);
        return services;
    }

    /// <summary>
    /// Checks the listen addresses (loopback only) and the paths (fail fast), logs the startup configuration and builds
    /// the pipeline: gzip, the control page (<c>wwwroot</c>), <c>/assets</c> from the repository's assets folder, endpoints.
    /// </summary>
    /// <exception cref="InvalidOperationException">A non-loopback listen address, or a missing data/assets folder.</exception>
    public static WebApplication UseStarshipServer(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (LoopbackBinding.Validate(app.Configuration) is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        if (ContentRootLocator.CheckControlPage(app.Environment.WebRootPath) is { } missing)
        {
            throw new InvalidOperationException(missing);
        }

        var paths = app.Services.GetRequiredService<StarshipPaths>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("StarshipMissionTracker.Api.Startup");
        LogConfiguration(app, paths, logger);

        app.UseResponseCompression();

        // 1) wwwroot (control page at /control/index.html, no RequestPath). 2) the repository's assets/ at /assets.
        app.UseStaticFiles();
        var assets = new PhysicalFileProvider(paths.AssetsDirectory);
        app.Lifetime.ApplicationStopped.Register(assets.Dispose);
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".dae"] = ColladaMediaType;
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = assets,
            RequestPath = ServerUrls.AssetsRequestPath,
            ContentTypeProvider = contentTypes,
        });

        app.MapStarshipEndpoints();

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = app.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
            var list = addresses is { Count: > 0 } ? string.Join(", ", addresses) : "(in-process test server)";
            logger.LogInformation("Listening on {Urls}; control page {ControlPath}, Google Earth master link {MasterPath}",
                list, ServerUrls.ControlPath, ServerUrls.MasterPath);
        });
        app.Lifetime.ApplicationStopping.Register(() => logger.LogInformation("Server stopping"));
        return app;
    }

    private static void ConfigureLogger(LoggerConfiguration logger, IConfiguration configuration, StarshipPaths paths) =>
        logger
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Information,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                paths.LogFilePattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture);

    private static void LogConfiguration(WebApplication app, StarshipPaths paths, Microsoft.Extensions.Logging.ILogger logger)
    {
        var simulation = app.Services.GetRequiredService<IOptions<SimulationOptions>>().Value;
        var links = app.Services.GetRequiredService<IOptions<NetworkLinkOptions>>().Value;
        var live = app.Services.GetRequiredService<IOptions<LiveOptions>>().Value;
        logger.LogInformation(
            "Starting Starship Mission Tracker server ({Environment}): root {Root}, database {DatabaseFile}, data {DataDirectory}, assets {AssetsDirectory}, logs {LogDirectory}",
            app.Environment.EnvironmentName, paths.RootDirectory, paths.DatabaseFile, paths.DataDirectory, paths.AssetsDirectory, paths.LogDirectory);
        logger.LogInformation(
            "Simulation: default speed {Speed}x, sample interval {SampleInterval} s, tick {Tick} ms, max failures {MaxFailures}, auto-start {AutoStart} (mission '{AutoStartMission}', countdown {Countdown} s)",
            simulation.DefaultSpeed, simulation.SampleIntervalSeconds, simulation.TickMilliseconds, simulation.MaxConsecutiveFailures,
            simulation.AutoStart, simulation.AutoStartMissionCode, simulation.AutoStartCountdownSeconds);
        logger.LogInformation(
            "NetworkLinks: planned {Planned} s, history {History} s, live {Live} s, min refresh {MinRefresh} s; live stale after {StaleAfter} s",
            links.PlannedRefreshSeconds, links.HistoryRefreshSeconds, links.LiveRefreshSeconds, links.MinRefreshPeriodSeconds, live.StaleAfterSeconds);
    }
}
