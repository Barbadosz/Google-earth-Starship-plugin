using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Data.Tests.Repositories;

public class MissionRepositoryTests
{
    [Fact]
    public async Task Import_ThenGetPlannedMission_RoundTripsEverythingWithProvenance()
    {
        using var db = await TestDatabase.CreateAsync();
        var source = TestData.Mission();

        Assert.True(await db.Missions.ImportAsync(source, isSimulated: true, Ct));
        var loaded = await db.Missions.GetPlannedMissionAsync(TestData.Code, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(source.Config.Name, loaded.Config.Name);
        Assert.Equal(source.Config.Description, loaded.Config.Description);
        Assert.Equal(source.Clock.LaunchTimeUtc, loaded.Clock.LaunchTimeUtc);
        Assert.Equal(2, loaded.Vehicles.Count);
        var starship = loaded.Vehicles.Single(v => v.Type == VehicleType.Starship);
        Assert.Equal("files/starship.dae", starship.ModelPath);
        var path = Assert.Single(starship.FlightPaths);
        var expected = source.Vehicles.Single(v => v.Type == VehicleType.Starship).FlightPaths[0];
        Assert.Equal(TrajectoryType.Planned, path.Type);
        Assert.Equal(DataQuality.Simulated, path.Quality);
        Assert.Equal("test source", path.SourceName);
        Assert.Equal("CC0", path.License);
        Assert.Equal(expected.Points, path.Points);
        Assert.Equal(source.Config.Events, loaded.Config.Events);
    }

    [Fact]
    public async Task Import_SimulatedData_IsNeverStoredAsMeasured()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), isSimulated: true, Ct);

        var loaded = await db.Missions.GetPlannedMissionAsync(TestData.Code, Ct);

        Assert.NotNull(loaded);
        Assert.All(loaded.Vehicles.SelectMany(v => v.FlightPaths), p =>
        {
            Assert.Equal(DataQuality.Simulated, p.Quality);
            Assert.All(p.Points, pt => Assert.Equal(DataQuality.Simulated, pt.Quality));
        });
        Assert.All(loaded.Config.Events, e => Assert.Equal(DataQuality.Simulated, e.Quality));
    }

    [Fact]
    public async Task Import_Twice_ReturnsFalseAndKeepsOneMission()
    {
        using var db = await TestDatabase.CreateAsync();

        Assert.True(await db.Missions.ImportAsync(TestData.Mission(), true, Ct));
        Assert.False(await db.Missions.ImportAsync(TestData.Mission(), true, Ct));

        Assert.Single(await db.Missions.ListAsync(Ct));
    }

    [Fact]
    public async Task Import_CreatesBothDataSources()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), true, Ct);

        await using var ctx = db.NewContext();
        var names = ctx.DataSources.Select(s => s.Name).OrderBy(n => n).ToList();

        Assert.Equal([DataSourceNames.InternalSimulator, DataSourceNames.PlannedTrajectoryFile], names);
    }

    [Fact]
    public async Task Import_PreservesFractionOfSecondOfT0AndUtc()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), false, Ct);

        var info = await db.Missions.GetInfoAsync(TestData.Code, Ct);

        Assert.NotNull(info);
        Assert.Equal(TestData.T0.UtcTicks, info.PlannedLaunchTimeUtc.UtcTicks);
        Assert.Equal(TimeSpan.Zero, info.PlannedLaunchTimeUtc.Offset);
        Assert.Equal(1234567, info.PlannedLaunchTimeUtc.Ticks % TimeSpan.TicksPerSecond);
        Assert.Equal(TimeSpan.Zero, info.UpdatedAtUtc.Offset);
        Assert.Equal(db.Time.Now, info.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetInfo_NewMission_IsScheduledWithoutActualLaunch()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), isSimulated: true, Ct);

        var info = await db.Missions.GetInfoAsync(TestData.Code, Ct);

        Assert.NotNull(info);
        Assert.Equal(MissionStatus.Scheduled, info.Status);
        Assert.Null(info.ActualLaunchTimeUtc);
        Assert.True(info.IsSimulated);
        Assert.Equal("Test mission", info.Name);
        Assert.Equal("A test mission", info.Description);
    }

    [Fact]
    public async Task GetInfoAndGetPlanned_UnknownCode_ReturnNull()
    {
        using var db = await TestDatabase.CreateAsync();

        Assert.Null(await db.Missions.GetInfoAsync("nope", Ct));
        Assert.Null(await db.Missions.GetPlannedMissionAsync("nope", Ct));
        Assert.Null(await db.Missions.GetCurrentInfoAsync(Ct));
    }

    [Fact]
    public async Task SetPlannedLaunchTime_ChangesClockAndKeepsFraction()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), true, Ct);
        db.Time.Now = db.Time.Now.AddMinutes(5);
        var newT0 = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(7654321);

        var ok = await db.Missions.SetPlannedLaunchTimeAsync(TestData.Code, newT0, Ct);

        Assert.True(ok);
        var info = await db.Missions.GetInfoAsync(TestData.Code, Ct);
        Assert.Equal(newT0, info!.PlannedLaunchTimeUtc);
        Assert.Equal(db.Time.Now, info.UpdatedAtUtc);
        var mission = await db.Missions.GetPlannedMissionAsync(TestData.Code, Ct);
        Assert.Equal(newT0, mission!.Clock.LaunchTimeUtc);
        Assert.Equal(newT0, mission.Config.LaunchTimeUtc);
    }

    [Fact]
    public async Task SetPlannedLaunchTime_UnknownMission_ReturnsFalse()
    {
        using var db = await TestDatabase.CreateAsync();

        Assert.False(await db.Missions.SetPlannedLaunchTimeAsync("nope", TestData.T0, Ct));
    }

    [Fact]
    public async Task SetPlannedLaunchTime_NonUtcOffset_Throws()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), true, Ct);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            db.Missions.SetPlannedLaunchTimeAsync(TestData.Code, new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(2)), Ct));
    }

    [Fact]
    public async Task SetFlightState_UpdatesStatusAndTimes()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), true, Ct);
        var launch = new DateTimeOffset(2026, 11, 15, 12, 5, 0, TimeSpan.Zero);

        var ok = await db.Missions.SetFlightStateAsync(TestData.Code, MissionStatus.InFlight, launch, launch, Ct);

        Assert.True(ok);
        var info = await db.Missions.GetInfoAsync(TestData.Code, Ct);
        Assert.Equal(MissionStatus.InFlight, info!.Status);
        Assert.Equal(launch, info.ActualLaunchTimeUtc);
        Assert.Equal(launch, info.PlannedLaunchTimeUtc);
    }

    [Fact]
    public async Task SetFlightState_NullTimes_KeepExistingValues()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), true, Ct);

        await db.Missions.SetFlightStateAsync(TestData.Code, MissionStatus.Completed, null, null, Ct);

        var info = await db.Missions.GetInfoAsync(TestData.Code, Ct);
        Assert.Equal(MissionStatus.Completed, info!.Status);
        Assert.Equal(TestData.T0, info.PlannedLaunchTimeUtc);
        Assert.Null(info.ActualLaunchTimeUtc);
        Assert.False(await db.Missions.SetFlightStateAsync("nope", MissionStatus.Completed, null, null, Ct));
    }

    [Fact]
    public async Task GetCurrentInfo_PrefersInFlight_ElseNewestByT0()
    {
        using var db = await TestDatabase.CreateAsync();
        var older = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await db.Missions.ImportAsync(TestData.Mission("a-old", older), true, Ct);
        await db.Missions.ImportAsync(TestData.Mission("b-new", newer), true, Ct);

        Assert.Equal("b-new", (await db.Missions.GetCurrentInfoAsync(Ct))!.MissionCode);

        await db.Missions.SetFlightStateAsync("a-old", MissionStatus.InFlight, older, null, Ct);

        Assert.Equal("a-old", (await db.Missions.GetCurrentInfoAsync(Ct))!.MissionCode);
        Assert.Equal(["a-old", "b-new"], (await db.Missions.ListAsync(Ct)).Select(m => m.MissionCode).ToArray());
    }

    [Fact]
    public async Task Import_ActualPath_IsRejected()
    {
        using var db = await TestDatabase.CreateAsync();
        var point = new TrajectoryPoint(0, 26, -97, 10, null, DataQuality.Simulated, "s");
        var vehicle = new Vehicle("Starship", VehicleType.Starship, null,
            [new FlightPath(TrajectoryType.Actual, DataQuality.Simulated, "s", null, [point])]);
        var mission = new Mission(new MissionConfig("x", "X", TestData.T0, null, []), new LaunchClock(TestData.T0), [vehicle]);

        await Assert.ThrowsAsync<ArgumentException>(() => db.Missions.ImportAsync(mission, true, Ct));
        Assert.Empty(await db.Missions.ListAsync(Ct));
    }

    [Fact]
    public async Task Import_EventOfVehicleWithoutTrajectory_FailsAndStoresNothing()
    {
        using var db = await TestDatabase.CreateAsync();
        var mission = TestData.Mission(starshipOnly: true);
        var bad = new MissionEvent(5, EventType.BoosterLanding, "Landing", null, null, null, null, VehicleType.SuperHeavy, DataQuality.Simulated, "s");
        var config = new MissionConfig(mission.MissionCode, mission.Name, TestData.T0, null, [bad]);

        await Assert.ThrowsAsync<StarshipMissionTracker.Core.Exceptions.MissionDataException>(() =>
            db.Missions.ImportAsync(new Mission(config, mission.Clock, mission.Vehicles), true, Ct));
        Assert.Empty(await db.Missions.ListAsync(Ct));
    }
}
