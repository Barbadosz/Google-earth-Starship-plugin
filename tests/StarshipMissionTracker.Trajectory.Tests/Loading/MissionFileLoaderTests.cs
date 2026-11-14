using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Loading;

namespace StarshipMissionTracker.Trajectory.Tests.Loading;

public sealed class MissionFileLoaderTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "smt-loader-" + Guid.NewGuid().ToString("N"));
    private readonly MissionFileLoader _loader = new();

    public MissionFileLoaderTests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private Task<Mission> Load(string mission, string trajectories, DateTimeOffset? t0) =>
        _loader.LoadAsync(mission, trajectories, t0, TestContext.Current.CancellationToken);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarshipMissionTracker.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (.sln) not found.");
    }

    private static string DataPath(params string[] parts) => Path.Combine([RepoRoot, "data", .. parts]);

    /// <summary>Copies the demo data to a temp dir, applying a text replacement to one file.</summary>
    private (string Mission, string Trajectories) CopyDemo(string file, string from, string to)
    {
        File.Copy(DataPath("missions", "demo-ift.json"), Path.Combine(_temp, "demo-ift.json"));
        foreach (var f in Directory.GetFiles(DataPath("trajectories")))
        {
            File.Copy(f, Path.Combine(_temp, Path.GetFileName(f)));
        }

        var path = Path.Combine(_temp, file);
        var text = File.ReadAllText(path);
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to));
        return (Path.Combine(_temp, "demo-ift.json"), _temp);
    }

    [Fact]
    public async Task LoadAsync_DemoFiles_LoadsSimulatedPlannedMission()
    {
        var mission = await Load(
            DataPath("missions", "demo-ift.json"), DataPath("trajectories"), null);

        Assert.Equal("demo-ift", mission.MissionCode);
        Assert.Equal(2, mission.Vehicles.Count);
        Assert.Equal(12, mission.Config.Events.Count);
        Assert.All(mission.Config.Events, e => Assert.Equal(DataQuality.Simulated, e.Quality));
        Assert.Contains("NOT derived from official", mission.Config.Description);
        foreach (var vehicle in mission.Vehicles)
        {
            var path = Assert.Single(vehicle.FlightPaths);
            Assert.Equal(TrajectoryType.Planned, path.Type);
            Assert.Equal(DataQuality.Simulated, path.Quality);
            Assert.Contains("Illustrative", path.SourceName);
            Assert.All(path.Points, p => Assert.Equal(DataQuality.Simulated, p.Quality));
        }

        Assert.Equal(2099, mission.Clock.LaunchTimeUtc.Year);
    }

    [Fact]
    public async Task LoadAsync_Override_WinsOverFileValue()
    {
        var t0 = new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.Zero);

        var mission = await Load(
            DataPath("missions", "demo-ift.json"), DataPath("trajectories"), t0);

        Assert.Equal(t0, mission.Clock.LaunchTimeUtc);
        Assert.Equal(t0, mission.Config.LaunchTimeUtc);
    }

    [Fact]
    public async Task LoadAsync_NonUtcOverride_ThrowsMissionDataException()
    {
        var local = new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.FromHours(2));

        await Assert.ThrowsAsync<MissionDataException>(() => Load(
            DataPath("missions", "demo-ift.json"), DataPath("trajectories"), local));
    }

    [Fact]
    public async Task LoadAsync_NoT0AnywhereAndNoOverride_ThrowsNamingFileAndOption()
    {
        var (mission, dir) = CopyDemo("demo-ift.json", "  \"launchTimeUtc\": \"2099-01-01T00:00:00Z\",\n", string.Empty);

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, dir, null));

        Assert.Contains("launchTimeUtc", ex.Message);
        Assert.Contains("demo-ift.json", ex.Message);
        Assert.Contains("--launch-time", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_NoT0InFileButOverride_Loads()
    {
        var (mission, dir) = CopyDemo("demo-ift.json", "  \"launchTimeUtc\": \"2099-01-01T00:00:00Z\",\n", string.Empty);
        var t0 = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var loaded = await Load(mission, dir, t0);

        Assert.Equal(t0, loaded.Clock.LaunchTimeUtc);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("lpt1")]
    [InlineData("demo-ift\\n")]
    public async Task LoadAsync_ReservedOrNewlineMissionCode_ThrowsNamingField(string code)
    {
        var (mission, dir) = CopyDemo("demo-ift.json", "\"missionCode\": \"demo-ift\"", $"\"missionCode\": \"{code}\"");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, dir, null));

        Assert.Contains("missionCode", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_UnknownEnumValue_ThrowsNamingFileFieldAndValue()
    {
        var (mission, dir) = CopyDemo("demo-ift.json", "\"type\": \"MaxQ\"", "\"type\": \"MaxQQ\"");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, dir, null));

        Assert.Contains("demo-ift.json", ex.Message);
        Assert.Contains("events[1].type", ex.Message);
        Assert.Contains("MaxQQ", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_EnumIsCaseInsensitive()
    {
        var (mission, dir) = CopyDemo("demo-ift.json", "\"type\": \"MaxQ\"", "\"type\": \"maxq\"");

        var loaded = await Load(mission, dir, null);

        Assert.Contains(loaded.Config.Events, e => e.Type == EventType.MaxQ);
    }

    [Fact]
    public async Task LoadAsync_NumericEnumValue_Rejected()
    {
        var (mission, dir) = CopyDemo("demo-ift-starship-planned.json", "\"quality\": \"Simulated\",", "\"quality\": \"1\",");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, dir, null));

        Assert.Contains("demo-ift-starship-planned.json", ex.Message);
        Assert.Contains("quality", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_LaunchTimeWithoutZ_ThrowsNamingFieldAndValue()
    {
        var (mission, dir) = CopyDemo("demo-ift.json", "2099-01-01T00:00:00Z", "2099-01-01T00:00:00+02:00");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, dir, null));

        Assert.Contains("launchTimeUtc", ex.Message);
        Assert.Contains("2099-01-01T00:00:00+02:00", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_NonIncreasingOffsets_ThrowsMissionDataException()
    {
        var (mission, dir) = CopyDemo("demo-ift-starship-planned.json", "\"t\": 60,", "\"t\": 30,");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, dir, null));

        Assert.Contains("demo-ift-starship-planned.json", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_MissingTrajectoryFile_ThrowsNamingFile()
    {
        var (mission, dir) = CopyDemo("demo-ift.json", "demo-ift", "demo-ift");
        File.Delete(Path.Combine(dir, "demo-ift-superheavy-planned.json"));

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, dir, null));

        Assert.Contains("demo-ift-superheavy-planned.json", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_MissingMissionFile_ThrowsMissionDataException()
    {
        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(
            Path.Combine(_temp, "nope.json"), DataPath("trajectories"), null));

        Assert.Contains("nope.json", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_MalformedJson_ThrowsMissionDataException()
    {
        var path = Path.Combine(_temp, "bad.json");
        await File.WriteAllTextAsync(path, "{ not json", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(path, DataPath("trajectories"), null));

        Assert.Contains("bad.json", ex.Message);
    }
}
