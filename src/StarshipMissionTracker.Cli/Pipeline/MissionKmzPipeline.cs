using StarshipMissionTracker.Cli.Options;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml;
using StarshipMissionTracker.Kml.Packaging;
using StarshipMissionTracker.Kml.Styles;
using StarshipMissionTracker.Trajectory.Calculation;
using StarshipMissionTracker.Trajectory.Loading;
using ILogger = Serilog.ILogger;

namespace StarshipMissionTracker.Cli.Pipeline;

/// <summary>
/// Composition of the M1 pipeline: load mission (+ T0 override) -> densify each path -> derive missing orientation
/// -> FlightPath with quality = worst point quality -> KML document -> KMZ with doc.kml, models and icons.
/// Library errors arrive as exceptions (MissionDataException, IOException); the caller logs and maps them.
/// </summary>
public sealed class MissionKmzPipeline
{
    /// <summary>Model used when the mission file names none (relative to the assets folder).</summary>
    public static string DefaultModelPath(VehicleType type) =>
        type == VehicleType.Starship ? "models/starship.dae" : "models/superheavy.dae";

    private const string KmzFilesPrefix = "files/";

    private readonly ILogger _logger;
    private readonly IMissionLoader _loader;
    private readonly FlightPathPreparer _preparer;
    private readonly IKmzPackager _packager;
    private readonly TrajectoryDensificationOptions _densification;

    public MissionKmzPipeline(
        ILogger logger,
        IMissionLoader? loader = null,
        ITrajectoryProcessor? processor = null,
        IKmzPackager? packager = null,
        TrajectoryDensificationOptions? densification = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loader = loader ?? new MissionFileLoader();
        _packager = packager ?? new KmzPackager();
        _densification = densification ?? TrajectoryDensificationOptions.Default;
        _preparer = new FlightPathPreparer(processor, _densification);
    }

    /// <summary>Runs the whole pipeline and writes <c>&lt;output&gt;/&lt;code&gt;.kmz</c> (replaced atomically).</summary>
    public async Task<(KmzBuildResult Result, string OutputPath)> RunAsync(CliOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var outputDir = Path.GetFullPath(options.OutputDirectory);
        var outputPath = Path.Combine(outputDir, options.OutputFileName);
        var tempPath = outputPath + ".tmp";

        // Build fully in memory first: a failed run must not leave an output folder or temp file behind.
        using var memory = new MemoryStream();
        var result = await BuildAsync(options, memory, ct).ConfigureAwait(false);

        Directory.CreateDirectory(outputDir);
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                memory.Position = 0;
                await memory.CopyToAsync(stream, ct).ConfigureAwait(false);
            }

            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        _logger.Information("KMZ written: {OutputFile} ({SizeBytes} bytes)", Display(outputPath), result.SizeBytes);
        return (result, outputPath);
    }

    /// <summary>Builds the KMZ into <paramref name="output"/> (no file is written).</summary>
    public async Task<KmzBuildResult> BuildAsync(CliOptions options, Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        var (mission, densified) = await LoadAndPrepareCoreAsync(options, ct).ConfigureAwait(false);
        var docKml = new MissionKmlBuilder(_densification, densified).Build(mission);
        var files = CollectAssets(mission, options.AssetsDirectory);
        _logger.Debug("KML built: {Characters} characters, {FileCount} attached files", docKml.Length, files.Count);

        var start = output.CanSeek ? output.Position : 0;
        _packager.Write(output, docKml, files);
        var size = output.CanSeek ? output.Position - start : -1;

        var entries = new List<string> { KmzPackager.DocEntryName };
        entries.AddRange(files.Select(f => f.RelativePath).Order(StringComparer.Ordinal));
        return new KmzBuildResult(
            mission.MissionCode,
            mission.Clock.LaunchTimeUtc,
            mission.Vehicles.Count,
            mission.Vehicles.SelectMany(v => v.FlightPaths).Sum(p => p.Points.Count),
            mission.Config.Events.Count,
            entries,
            size);
    }

    /// <summary>Loads the mission and prepares every path for KML (densified, orientation filled, model path set).</summary>
    public async Task<Mission> LoadAndPrepareAsync(CliOptions options, CancellationToken ct = default) =>
        (await LoadAndPrepareCoreAsync(options, ct).ConfigureAwait(false)).Mission;

    /// <summary>As <see cref="LoadAndPrepareAsync"/>, plus the prepared paths that really got points inserted.</summary>
    private async Task<(Mission Mission, IReadOnlySet<FlightPath> DensifiedPaths)> LoadAndPrepareCoreAsync(
        CliOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var dataDir = Path.GetFullPath(options.DataDirectory);
        var missionFile = Path.Combine(dataDir, "missions", options.MissionCode + ".json");
        var trajectoriesDir = Path.Combine(dataDir, "trajectories");
        _logger.Debug("Loading mission file {MissionFile}, trajectories from {TrajectoriesDirectory}", missionFile, trajectoriesDir);

        var loaded = await _loader.LoadAsync(missionFile, trajectoriesDir, options.LaunchTimeOverrideUtc, ct)
            .ConfigureAwait(false);
        _logger.Information(
            "Mission {MissionCode} loaded: {VehicleCount} vehicles, {EventCount} events, T0 {LaunchTime} ({T0Source})",
            loaded.MissionCode, loaded.Vehicles.Count, loaded.Config.Events.Count, loaded.Clock.Format(0),
            options.LaunchTimeOverrideUtc is null ? "mission file" : "--launch-time override");

        var assetsDir = Path.GetFullPath(options.AssetsDirectory);
        var densified = new HashSet<FlightPath>(ReferenceEqualityComparer.Instance);
        var vehicles = loaded.Vehicles.Select(v => PrepareVehicle(v, assetsDir, densified)).ToList();
        return (new Mission(loaded.Config, loaded.Clock, vehicles), densified);
    }

    private Vehicle PrepareVehicle(Vehicle vehicle, string assetsDir, HashSet<FlightPath> densified)
    {
        var paths = vehicle.FlightPaths.Select(p => PreparePath(vehicle, p, densified)).ToList();

        var modelPath = vehicle.ModelPath;
        if (modelPath is null)
        {
            var fallback = DefaultModelPath(vehicle.Type);
            if (File.Exists(Path.Combine(assetsDir, fallback)))
            {
                modelPath = fallback;
                _logger.Debug("{Vehicle}: no modelPath in the mission file, using default model {ModelPath}", vehicle.Name, fallback);
            }
            else
            {
                _logger.Warning("{Vehicle}: no model file ({ModelPath} not found in the assets folder); 3D model folder stays empty",
                    vehicle.Name, fallback);
            }
        }

        return new Vehicle(vehicle.Name, vehicle.Type, modelPath, paths);
    }

    private FlightPath PreparePath(Vehicle vehicle, FlightPath path, HashSet<FlightPath> insertedSet)
    {
        var prepared = _preparer.Prepare(path);
        _logger.Information(
            "{Vehicle} {TrajectoryType} path: {SourcePoints} -> {Points} points after densification, quality {SourceQuality} -> {Quality}",
            vehicle.Name, path.Type, path.Points.Count, prepared.Points.Count, path.Quality, prepared.Quality);
        if (prepared.Points.Count > path.Points.Count)
        {
            insertedSet.Add(prepared);
        }

        return prepared;
    }

    /// <summary>Model files (as <c>files/&lt;file name&gt;</c>, matching the Model hrefs) and the style icons.</summary>
    private List<KmzEntry> CollectAssets(Mission mission, string assetsDirectory)
    {
        var assetsDir = Path.GetFullPath(assetsDirectory);
        var entries = new Dictionary<string, KmzEntry>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var vehicle in mission.Vehicles)
        {
            if (vehicle.ModelPath is null)
            {
                continue;
            }

            // VehicleGenerator writes Link href "files/<last segment of ModelPath>".
            var entryPath = KmzFilesPrefix + vehicle.ModelPath.Split('/')[^1];
            AddFile(entries, sources, entryPath, Path.Combine(assetsDir, vehicle.ModelPath), $"3D model of {vehicle.Name}");
        }

        foreach (var href in KmlStyles.IconHrefs)
        {
            // Icon hrefs are "files/icons/<name>.png" -> source "<assets>/icons/<name>.png".
            AddFile(entries, sources, href, Path.Combine(assetsDir, href[KmzFilesPrefix.Length..]), "icon");
        }

        return entries.Values.ToList();
    }

    private void AddFile(Dictionary<string, KmzEntry> entries, Dictionary<string, string> sources, string entryPath, string sourcePath, string what)
    {
        if (sources.TryGetValue(entryPath, out var existingSource))
        {
            if (!string.Equals(existingSource, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new MissionDataException(
                    $"Two different asset files would be packaged as '{entryPath}': '{Display(existingSource)}' and '{Display(sourcePath)}'. "
                    + "Give the models different file names.");
            }

            return;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                $"Asset not found: {what} '{Display(sourcePath)}' (check the --assets folder).", sourcePath);
        }

        var bytes = File.ReadAllBytes(sourcePath);
        entries.Add(entryPath, new KmzEntry(entryPath, bytes));
        sources.Add(entryPath, sourcePath);
        _logger.Debug("Packaging {Asset} as {EntryPath} ({SizeBytes} bytes)", Display(sourcePath), entryPath, bytes.Length);
    }

    /// <summary>Path relative to the current directory when it lies below it (keeps logs free of user folders).</summary>
    public static string Display(string path)
    {
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), full);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? full : relative;
    }
}
