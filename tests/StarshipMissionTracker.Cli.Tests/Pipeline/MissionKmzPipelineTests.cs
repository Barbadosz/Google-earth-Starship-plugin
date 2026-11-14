using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using StarshipMissionTracker.Cli.Options;
using StarshipMissionTracker.Cli.Pipeline;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Cli.Tests.Pipeline;

public sealed class MissionKmzPipelineTests : IDisposable
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly XNamespace Gx = KmlXml.Gx;
    private static readonly DateTimeOffset Override = new(2026, 12, 1, 14, 0, 0, TimeSpan.Zero);

    private readonly List<string> _tempDirs = [];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarshipMissionTracker.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (.sln) not found.");
    }

    private static CliOptions DemoOptions(DateTimeOffset? launchTime = null, string? dataDir = null) =>
        new("demo-ift", launchTime,
            dataDir ?? Path.Combine(RepoRoot(), "data"),
            Path.Combine(RepoRoot(), "assets"),
            Path.Combine(Path.GetTempPath(), "unused-output"));

    private static MissionKmzPipeline CreatePipeline() => new(Serilog.Core.Logger.None);

    private static async Task<(KmzBuildResult Result, Dictionary<string, byte[]> Entries)> BuildInMemoryAsync(CliOptions options)
    {
        using var stream = new MemoryStream();
        var result = await CreatePipeline().BuildAsync(options, stream, TestContext.Current.CancellationToken);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            using var s = entry.Open();
            using var copy = new MemoryStream();
            await s.CopyToAsync(copy, TestContext.Current.CancellationToken);
            entries[entry.FullName] = copy.ToArray();
        }

        return (result, entries);
    }

    private static XDocument DocKml(Dictionary<string, byte[]> entries) =>
        XDocument.Parse(System.Text.Encoding.UTF8.GetString(entries["doc.kml"]));

    [Fact]
    public async Task BuildAsync_DemoData_KmzHasDocAndExpectedFiles()
    {
        var (result, entries) = await BuildInMemoryAsync(DemoOptions(Override));

        Assert.Equal(
            ["doc.kml", "files/icons/booster.png", "files/icons/starship.png", "files/starship.dae", "files/superheavy.dae"],
            entries.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(entries.Keys.Order(StringComparer.Ordinal), result.EntryNames.Order(StringComparer.Ordinal));
        Assert.Equal(2, result.VehicleCount);
        Assert.True(result.SizeBytes > 0);

        var assetBytes = await File.ReadAllBytesAsync(
            Path.Combine(RepoRoot(), "assets", "models", "starship.dae"), TestContext.Current.CancellationToken);
        Assert.Equal(assetBytes, entries["files/starship.dae"]);
    }

    [Fact]
    public async Task BuildAsync_DemoData_EveryHrefPointsToAnEntry()
    {
        var (_, entries) = await BuildInMemoryAsync(DemoOptions(Override));
        var doc = DocKml(entries);

        var hrefs = doc.Descendants(K + "href").Select(h => Uri.UnescapeDataString(h.Value))
            .Where(h => !h.StartsWith("http", StringComparison.Ordinal))
            .Distinct()
            .ToList();
        Assert.Contains("files/starship.dae", hrefs);
        Assert.Contains("files/superheavy.dae", hrefs);
        Assert.All(hrefs, h => Assert.True(entries.ContainsKey(h), $"KMZ entry missing for href '{h}'"));
    }

    [Fact]
    public async Task BuildAsync_LaunchTimeOverride_WinsOverFile_AndRetimesTracks()
    {
        var (result, entries) = await BuildInMemoryAsync(DemoOptions(Override));
        var doc = DocKml(entries);

        Assert.Equal(Override, result.LaunchTimeUtc);
        var tracks = doc.Descendants(Gx + "Track").ToList();
        Assert.NotEmpty(tracks);
        // Densified offsets can be fractional, so the tracks may use the millisecond form.
        Assert.All(tracks, t => Assert.Equal(
            Override,
            DateTimeOffset.Parse(t.Elements(K + "when").First().Value, System.Globalization.CultureInfo.InvariantCulture)));
        var liftoff = doc.Descendants(K + "Placemark").First(p => p.Element(K + "name")?.Value == "Liftoff");
        Assert.Equal("2026-12-01T14:00:00Z", liftoff.Element(K + "TimeStamp")!.Element(K + "when")!.Value);
    }

    [Fact]
    public async Task BuildAsync_NoOverride_UsesFileLaunchTime()
    {
        var (result, _) = await BuildInMemoryAsync(DemoOptions());
        Assert.Equal(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), result.LaunchTimeUtc);
    }

    [Fact]
    public async Task LoadAndPrepareAsync_PathsDensified_QualityNotBetterThanWorstPoint_ModelsSet()
    {
        var mission = await CreatePipeline().LoadAndPrepareAsync(DemoOptions(Override), TestContext.Current.CancellationToken);

        Assert.Equal(2, mission.Vehicles.Count);
        foreach (var vehicle in mission.Vehicles)
        {
            Assert.Equal(MissionKmzPipeline.DefaultModelPath(vehicle.Type), vehicle.ModelPath);
            var path = vehicle.FlightPaths[0];
            Assert.All(path.Points, p => Assert.False(path.Quality.IsBetterThan(p.Quality)));
            Assert.All(path.Points, p => Assert.True(p.HasOrientation));
        }

        var superHeavy = mission.Vehicles.Single(v => v.Type == VehicleType.SuperHeavy);
        Assert.True(superHeavy.FlightPaths[0].Points.Count > 34, "Super Heavy path should be densified");
    }

    [Fact]
    public async Task BuildAsync_NoLaunchTimeAnywhere_ThrowsMissionDataException()
    {
        var dataDir = CopyDemoDataWithoutLaunchTime();

        var ex = await Assert.ThrowsAsync<MissionDataException>(() =>
            BuildInMemoryAsync(DemoOptions(null, dataDir)));
        Assert.Contains("--launch-time", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_NoLaunchTimeInFile_OverrideWorks()
    {
        var dataDir = CopyDemoDataWithoutLaunchTime();

        var (result, _) = await BuildInMemoryAsync(DemoOptions(Override, dataDir));
        Assert.Equal(Override, result.LaunchTimeUtc);
    }

    [Fact]
    public async Task BuildAsync_MissingAssets_ThrowsFileNotFound()
    {
        var emptyAssets = NewTempDir();
        var options = DemoOptions(Override) with { AssetsDirectory = emptyAssets };

        await Assert.ThrowsAsync<FileNotFoundException>(() => BuildInMemoryAsync(options));
    }

    [Fact]
    public async Task RunAsync_WritesKmzFile()
    {
        var outDir = NewTempDir();
        var options = DemoOptions(Override) with { OutputDirectory = Path.Combine(outDir, "nested") };

        var (result, path) = await CreatePipeline().RunAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(outDir, "nested", "demo-ift.kmz"), path);
        Assert.Equal(result.SizeBytes, new FileInfo(path).Length);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task RunAsync_FailedRun_LeavesNoOutputDirectoryOrTempFile()
    {
        var outDir = Path.Combine(NewTempDir(), "never-created");
        var options = DemoOptions(Override) with { MissionCode = "does-not-exist", OutputDirectory = outDir };

        await Assert.ThrowsAsync<MissionDataException>(() =>
            CreatePipeline().RunAsync(options, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(outDir));
    }

    [Fact]
    public async Task BuildAsync_SameModelFileNameFromDifferentFolders_Throws()
    {
        var dataDir = CopyDemoDataWithModelPaths("a/ship.dae", "b/ship.dae");
        var assets = NewTempDir();
        Directory.CreateDirectory(Path.Combine(assets, "a"));
        Directory.CreateDirectory(Path.Combine(assets, "b"));
        File.WriteAllText(Path.Combine(assets, "a", "ship.dae"), "<COLLADA/>");
        File.WriteAllText(Path.Combine(assets, "b", "ship.dae"), "<COLLADA/>");
        var options = DemoOptions(Override, dataDir) with { AssetsDirectory = assets };

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => BuildInMemoryAsync(options));
        Assert.Contains("files/ship.dae", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_DemoData_DensificationNoteOnlyOnPathsWithInsertedPoints()
    {
        var (_, entries) = await BuildInMemoryAsync(DemoOptions(Override));
        var kml = System.Text.Encoding.UTF8.GetString(entries["doc.kml"]);
        Assert.Contains("Densified", kml);

        // A spacing so large that nothing is inserted: no densification statement anywhere.
        using var stream = new MemoryStream();
        var pipeline = new MissionKmzPipeline(
            Serilog.Core.Logger.None,
            densification: new StarshipMissionTracker.Core.Models.TrajectoryDensificationOptions(1e9, 1e9));
        await pipeline.BuildAsync(DemoOptions(Override), stream, TestContext.Current.CancellationToken);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("doc.kml")!.Open());
        var sparse = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Densified", sparse);
    }

    private string CopyDemoDataWithModelPaths(string starshipModel, string boosterModel)
    {
        var root = CopyDemoDataWithoutLaunchTime();
        var file = Path.Combine(root, "missions", "demo-ift.json");
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        var vehicles = json["vehicles"]!.AsArray();
        vehicles[0]!["modelPath"] = starshipModel;
        vehicles[1]!["modelPath"] = boosterModel;
        File.WriteAllText(file, json.ToJsonString());
        return root;
    }

    private string CopyDemoDataWithoutLaunchTime()
    {
        var root = NewTempDir();
        var missions = Directory.CreateDirectory(Path.Combine(root, "missions")).FullName;
        var trajectories = Directory.CreateDirectory(Path.Combine(root, "trajectories")).FullName;
        var source = Path.Combine(RepoRoot(), "data");

        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "missions", "demo-ift.json")))!.AsObject();
        json.Remove("launchTimeUtc");
        File.WriteAllText(Path.Combine(missions, "demo-ift.json"), json.ToJsonString());
        foreach (var file in Directory.GetFiles(Path.Combine(source, "trajectories"), "demo-ift-*.json"))
        {
            File.Copy(file, Path.Combine(trajectories, Path.GetFileName(file)));
        }

        return root;
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "smt-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup of temp folders.
            }
        }
    }
}
