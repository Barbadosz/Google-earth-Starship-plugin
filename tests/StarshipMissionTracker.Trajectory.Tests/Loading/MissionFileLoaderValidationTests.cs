using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Loading;

namespace StarshipMissionTracker.Trajectory.Tests.Loading;

/// <summary>Input validation of <see cref="MissionFileLoader"/>: error messages must name the right file, index and field.</summary>
public sealed class MissionFileLoaderValidationTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "smt-loadval-" + Guid.NewGuid().ToString("N"));
    private readonly MissionFileLoader _loader = new();

    public MissionFileLoaderValidationTests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private Task<Mission> Load(string mission, DateTimeOffset? t0) =>
        _loader.LoadAsync(mission, _temp, t0, TestContext.Current.CancellationToken);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarshipMissionTracker.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (.sln) not found.");
    }

    /// <summary>Copies the demo data into the temp dir, applying one text replacement to one file; returns the mission path.</summary>
    private string CopyDemo(string file, string from, string to)
    {
        var root = FindRepoRoot();
        File.Copy(Path.Combine(root, "data", "missions", "demo-ift.json"), Path.Combine(_temp, "demo-ift.json"));
        foreach (var f in Directory.GetFiles(Path.Combine(root, "data", "trajectories")))
        {
            File.Copy(f, Path.Combine(_temp, Path.GetFileName(f)));
        }

        var path = Path.Combine(_temp, file);
        var text = File.ReadAllText(path);
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to));
        return Path.Combine(_temp, "demo-ift.json");
    }

    [Theory]
    [InlineData("Liftoff,MaxQ")]
    [InlineData(" MaxQ")]
    [InlineData("MaxQ ")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("")]
    public async Task EnumLookalikes_Rejected(string value)
    {
        var mission = CopyDemo("demo-ift.json", "\"type\": \"MaxQ\"", $"\"type\": \"{value}\"");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.Contains("events[1].type", ex.Message);
    }

    [Theory]
    [InlineData("..\\\\evil")]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("-lead")]
    [InlineData("_lead")]
    [InlineData("has space")]
    [InlineData("C:\\\\x")]
    public async Task UnsafeMissionCode_Rejected(string code)
    {
        var mission = CopyDemo("demo-ift.json", "\"missionCode\": \"demo-ift\"", $"\"missionCode\": \"{code}\"");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.Contains("missionCode", ex.Message);
        Assert.Contains("demo-ift.json", ex.Message);
    }

    [Fact]
    public async Task SafeMissionCodeWithDashAndUnderscore_Accepted()
    {
        var mission = CopyDemo("demo-ift.json", "\"missionCode\": \"demo-ift\"", "\"missionCode\": \"Demo_1-x\"");
        File.Move(Path.Combine(_temp, "demo-ift-starship-planned.json"), Path.Combine(_temp, "Demo_1-x-starship-planned.json"));
        File.Move(Path.Combine(_temp, "demo-ift-superheavy-planned.json"), Path.Combine(_temp, "Demo_1-x-superheavy-planned.json"));

        var loaded = await Load(mission, null);

        Assert.Equal("Demo_1-x", loaded.MissionCode);
    }

    [Fact]
    public async Task PointLatitudeOutOfRange_NamesTrajectoryFileIndexAndField()
    {
        var mission = CopyDemo("demo-ift-starship-planned.json", "\"t\": 30, \"lat\": 25.9972", "\"t\": 30, \"lat\": 95");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.StartsWith("demo-ift-starship-planned.json: points[3].lat = 95:", ex.Message);
    }

    [Fact]
    public async Task PointMissingNumber_NamesIndexedField()
    {
        var mission = CopyDemo("demo-ift-superheavy-planned.json", "\"t\": 30, \"lat\": 25.9972, \"lon\": -97.1511,", "\"t\": 30, \"lat\": 25.9972,");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.Contains("demo-ift-superheavy-planned.json", ex.Message);
        Assert.Contains("points[3].lon", ex.Message);
    }

    [Fact]
    public async Task PointWithInvalidRoll_NamesFileIndexAndField()
    {
        var mission = CopyDemo("demo-ift-starship-planned.json", "\"t\": 20, \"lat\"", "\"t\": 20, \"rollDeg\": 200, \"lat\"");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.StartsWith("demo-ift-starship-planned.json: points[2].rollDeg = 200:", ex.Message);
    }

    [Theory]
    [InlineData("\"lon\": -97.0981, ")]
    [InlineData("\"lat\": 25.9972, \"lon\": -97.0981, \"altM\": 12000, ")]
    public async Task EventWithOnlyLatOrLonOrNone_LoadsOrNamesEventIndex(string removed)
    {
        // Removing both lat and lon (and altM) is a valid position-less event; removing only lon is not.
        var mission = CopyDemo("demo-ift.json", removed, string.Empty);

        if (removed.Contains("\"lat\"", StringComparison.Ordinal))
        {
            Assert.Null((await Load(mission, null)).Config.Events[1].Latitude);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));
            Assert.StartsWith("demo-ift.json: events[1]", ex.Message);
        }
    }

    [Fact]
    public async Task EventWithOnlyLongitude_NamesEventIndex()
    {
        var mission = CopyDemo("demo-ift.json", "\"lat\": 25.9972, \"lon\": -97.0981", "\"lon\": -97.0981");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.StartsWith("demo-ift.json: events[1]", ex.Message);
    }

    [Fact]
    public async Task EventLatitudeOutOfRange_NamesEventIndexAndField()
    {
        var mission = CopyDemo("demo-ift.json", "\"lat\": 25.9972, \"lon\": -97.0981", "\"lat\": 95, \"lon\": -97.0981");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.StartsWith("demo-ift.json: events[1].lat = 95:", ex.Message);
    }

    [Fact]
    public async Task DuplicateVehicleTypes_ThrowsMissionDataException()
    {
        var mission = CopyDemo("demo-ift.json", "\"type\": \"SuperHeavy\", \"name\"", "\"type\": \"Starship\", \"name\"");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.Contains("demo-ift.json", ex.Message);
        Assert.Contains("Starship", ex.Message);
    }

    [Fact]
    public async Task EmptyPointList_ThrowsNamingFileAndField()
    {
        var mission = CopyDemo("demo-ift.json", "demo-ift", "demo-ift");
        File.WriteAllText(
            Path.Combine(_temp, "demo-ift-starship-planned.json"),
            "{ \"vehicle\": \"Starship\", \"quality\": \"Simulated\", \"source\": \"s\", \"points\": [] }");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.Contains("demo-ift-starship-planned.json", ex.Message);
        Assert.Contains("points", ex.Message);
    }

    [Fact]
    public async Task EmptyVehicleList_ThrowsNamingField()
    {
        var path = Path.Combine(_temp, "empty-vehicles.json");
        await File.WriteAllTextAsync(
            path,
            "{ \"missionCode\": \"x\", \"name\": \"n\", \"launchTimeUtc\": \"2099-01-01T00:00:00Z\", \"vehicles\": [] }",
            TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(path, null));

        Assert.Contains("empty-vehicles.json", ex.Message);
        Assert.Contains("vehicles", ex.Message);
    }

    [Fact]
    public async Task InvalidFileT0ButValidOverride_OverrideWins()
    {
        // Documented decision: the override wins and the file value is not parsed.
        var mission = CopyDemo("demo-ift.json", "2099-01-01T00:00:00Z", "not-a-time");
        var t0 = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var loaded = await Load(mission, t0);

        Assert.Equal(t0, loaded.Clock.LaunchTimeUtc);
        Assert.Equal(t0, loaded.Config.LaunchTimeUtc);
    }

    [Fact]
    public async Task InvalidFileT0WithoutOverride_StillRejected()
    {
        var mission = CopyDemo("demo-ift.json", "2099-01-01T00:00:00Z", "not-a-time");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.Contains("launchTimeUtc", ex.Message);
    }

    [Fact]
    public async Task UnknownFieldInMissionFile_Rejected()
    {
        var mission = CopyDemo("demo-ift.json", "\"missionCode\": \"demo-ift\",", "\"missionCode\": \"demo-ift\", \"lauchTimeUtc\": \"2099-01-01T00:00:00Z\",");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.Contains("demo-ift.json", ex.Message);
        Assert.Contains("lauchTimeUtc", ex.Message);
    }

    [Fact]
    public async Task UnknownFieldInTrajectoryFile_Rejected()
    {
        var mission = CopyDemo("demo-ift-starship-planned.json", "\"altM\": 200,", "\"altitudeM\": 200, \"altM\": 200,");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.Contains("demo-ift-starship-planned.json", ex.Message);
        Assert.Contains("altitudeM", ex.Message);
    }

    [Fact]
    public async Task TrajectoryVehicleDiffersFromFileName_Rejected()
    {
        var mission = CopyDemo("demo-ift-superheavy-planned.json", "\"vehicle\": \"SuperHeavy\"", "\"vehicle\": \"Starship\"");

        var ex = await Assert.ThrowsAsync<MissionDataException>(() => Load(mission, null));

        Assert.Contains("demo-ift-superheavy-planned.json", ex.Message);
        Assert.Contains("vehicle", ex.Message);
        Assert.Contains("SuperHeavy", ex.Message);
    }
}
