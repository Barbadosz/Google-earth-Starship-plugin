using Microsoft.Extensions.Logging.Abstractions;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Data.Import;
using StarshipMissionTracker.Trajectory.Loading;

namespace StarshipMissionTracker.Data.Tests.Import;

public class MissionImporterTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.sln").Concat(dir.GetFiles("*.slnx")).Any())
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine([dir.FullName, .. parts]);
    }

    private static string DemoMission => RepoFile("data", "missions", "demo-ift.json");
    private static string TrajectoryDir => RepoFile("data", "trajectories");

    [Fact]
    public async Task ImportIfMissing_DemoMissionTwice_ImportsOnceAndKeepsOneMission()
    {
        using var db = await TestDatabase.CreateAsync();
        var importer = new MissionImporter(new MissionFileLoader(), db.Missions, NullLogger<MissionImporter>.Instance);

        Assert.True(await importer.ImportIfMissingAsync(DemoMission, TrajectoryDir, Ct));
        Assert.False(await importer.ImportIfMissingAsync(DemoMission, TrajectoryDir, Ct));

        var info = Assert.Single(await db.Missions.ListAsync(Ct));
        Assert.Equal("demo-ift", info.MissionCode);
        Assert.True(info.IsSimulated);
        Assert.Equal(MissionStatus.Scheduled, info.Status);
        Assert.Equal(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), info.PlannedLaunchTimeUtc);
    }

    [Fact]
    public async Task ImportIfMissing_DemoMission_StoresBothVehiclesAndEventsWithProvenance()
    {
        using var db = await TestDatabase.CreateAsync();
        var importer = new MissionImporter(new MissionFileLoader(), db.Missions, NullLogger<MissionImporter>.Instance);
        await importer.ImportIfMissingAsync(DemoMission, TrajectoryDir, Ct);

        var loaded = await db.Missions.GetPlannedMissionAsync("demo-ift", Ct);
        var direct = await new MissionFileLoader().LoadAsync(DemoMission, TrajectoryDir, null, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Vehicles.Count);
        Assert.Equal(direct.Config.Events, loaded.Config.Events);
        foreach (var vehicle in direct.Vehicles)
        {
            var stored = loaded.Vehicles.Single(v => v.Type == vehicle.Type);
            Assert.Equal(vehicle.FlightPaths[0].Points, stored.FlightPaths[0].Points);
            Assert.Equal(vehicle.FlightPaths[0].Quality, stored.FlightPaths[0].Quality);
        }

        Assert.All(loaded.Vehicles.SelectMany(v => v.FlightPaths), p => Assert.NotEqual(DataQuality.Measured, p.Quality));
    }

    [Fact]
    public async Task ImportIfMissing_AfterT0Change_DoesNotReapplyFileLaunchTime()
    {
        using var db = await TestDatabase.CreateAsync();
        var importer = new MissionImporter(new MissionFileLoader(), db.Missions, NullLogger<MissionImporter>.Instance);
        await importer.ImportIfMissingAsync(DemoMission, TrajectoryDir, Ct);
        var changed = new DateTimeOffset(2027, 5, 5, 5, 5, 5, TimeSpan.Zero);
        await db.Missions.SetPlannedLaunchTimeAsync("demo-ift", changed, Ct);

        Assert.False(await importer.ImportIfMissingAsync(DemoMission, TrajectoryDir, Ct));

        Assert.Equal(changed, (await db.Missions.GetInfoAsync("demo-ift", Ct))!.PlannedLaunchTimeUtc);
    }

    [Fact]
    public async Task ImportIfMissing_MixedQualityMission_IsNotSimulated()
    {
        using var db = await TestDatabase.CreateAsync();
        var mission = TestData.Mission(pathQuality: DataQuality.Estimated);
        var importer = new MissionImporter(new FakeLoader(mission), db.Missions, NullLogger<MissionImporter>.Instance);

        Assert.True(await importer.ImportIfMissingAsync("any.json", "dir", Ct));

        Assert.False((await db.Missions.GetInfoAsync(TestData.Code, Ct))!.IsSimulated);
    }

    [Fact]
    public async Task ImportIfMissing_LoaderFails_PropagatesAndStoresNothing()
    {
        using var db = await TestDatabase.CreateAsync();
        var importer = new MissionImporter(new MissionFileLoader(), db.Missions, NullLogger<MissionImporter>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => importer.ImportIfMissingAsync(RepoFile("data", "missions", "missing.json"), TrajectoryDir, Ct));

        Assert.Empty(await db.Missions.ListAsync(Ct));
    }

    private sealed class FakeLoader(Mission mission) : IMissionLoader
    {
        public Task<Mission> LoadAsync(string missionConfigPath, string trajectoriesDirectory, DateTimeOffset? launchTimeOverrideUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(mission);
    }
}
