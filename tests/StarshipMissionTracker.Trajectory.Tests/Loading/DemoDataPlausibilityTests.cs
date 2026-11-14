using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Calculation;
using StarshipMissionTracker.Trajectory.Interpolation;
using StarshipMissionTracker.Trajectory.Loading;

namespace StarshipMissionTracker.Trajectory.Tests.Loading;

/// <summary>
/// The demo data is illustrative (Simulated) but must not imply absurd motion: speeds, climb off the pad and landing
/// speeds are checked on the densified path, the way the KML writer will see it.
/// </summary>
public sealed class DemoDataPlausibilityTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarshipMissionTracker.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (.sln) not found.");
    }

    private static Task<Mission> LoadDemo() =>
        new MissionFileLoader().LoadAsync(
            Path.Combine(RepoRoot, "data", "missions", "demo-ift.json"),
            Path.Combine(RepoRoot, "data", "trajectories"),
            null,
            TestContext.Current.CancellationToken);

    private static IReadOnlyList<TrajectoryPoint> Densified(Vehicle vehicle) =>
        new TrajectoryProcessor().Densify(vehicle.FlightPaths[0].Points, TrajectoryDensificationOptions.Default);

    /// <summary>Per consecutive pair: (start offset, ground metres per second, 3D metres per second).</summary>
    private static List<(double T, double Ground, double Speed3D)> Speeds(IReadOnlyList<TrajectoryPoint> points)
    {
        var list = new List<(double, double, double)>();
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var dt = b.OffsetSeconds - a.OffsetSeconds;
            var ground = GeodesicCalculator.Inverse(a.Latitude, a.Longitude, b.Latitude, b.Longitude).DistanceMeters;
            var vertical = b.AltitudeMeters - a.AltitudeMeters;
            list.Add((a.OffsetSeconds, ground / dt, Math.Sqrt((ground * ground) + (vertical * vertical)) / dt));
        }

        return list;
    }

    [Fact]
    public async Task DemoVehicles_DensifiedPathIsValidSimulatedFlightPath()
    {
        var mission = await LoadDemo();

        foreach (var vehicle in mission.Vehicles)
        {
            var source = vehicle.FlightPaths[0];
            var dense = Densified(vehicle);

            Assert.Equal(DataQuality.Simulated, source.Quality);
            Assert.All(dense, p => Assert.Equal(DataQuality.Simulated, p.Quality));
            Assert.All(dense, p => Assert.True(p.AltitudeMeters >= 0, $"{vehicle.Type}: altitude {p.AltitudeMeters} at {p.OffsetSeconds}"));

            // Must satisfy the Core quality rules (never better than the worst point; Planned is never Measured).
            var path = new FlightPath(source.Type, source.Quality, source.SourceName, source.License, dense);
            Assert.Equal(TrajectoryType.Planned, path.Type);
        }
    }

    [Fact]
    public async Task DemoVehicles_ImpliedSpeedsPlausible()
    {
        var mission = await LoadDemo();

        foreach (var vehicle in mission.Vehicles)
        {
            var speeds = Speeds(Densified(vehicle));

            Assert.True(speeds.Max(s => s.Speed3D) < 8_000, $"{vehicle.Type}: max {speeds.Max(s => s.Speed3D):F0} m/s");
            Assert.All(speeds.Where(s => s.T < 30), s => Assert.True(s.Ground < 300, $"{vehicle.Type}: ground {s.Ground:F0} m/s at T+{s.T}"));
        }
    }

    [Fact]
    public async Task DemoVehicles_SpeedChangesSmoothly()
    {
        var mission = await LoadDemo();

        foreach (var vehicle in mission.Vehicles)
        {
            // Compared only above 200 m/s: the standing start and the final touchdown change speed by large factors by nature.
            var speeds = Speeds(Densified(vehicle));
            for (var i = 1; i < speeds.Count; i++)
            {
                if (speeds[i].Speed3D < 200 || speeds[i - 1].Speed3D < 200)
                {
                    continue;
                }

                var ratio = speeds[i].Speed3D / speeds[i - 1].Speed3D;
                Assert.True(ratio is < 2 and > 0.5, $"{vehicle.Type}: speed jump x{ratio:F2} at T+{speeds[i].T}");
            }
        }
    }

    [Fact]
    public async Task DemoVehicles_TouchDownSlowly()
    {
        var mission = await LoadDemo();

        foreach (var vehicle in mission.Vehicles)
        {
            var last = Speeds(Densified(vehicle)).Last();

            Assert.True(last.Speed3D < 50, $"{vehicle.Type}: final speed {last.Speed3D:F0} m/s");
        }
    }

    [Fact]
    public async Task DemoEvents_MatchTheVehiclePathAtTheirOffsets()
    {
        var mission = await LoadDemo();
        var starship = mission.Vehicles.Single(v => v.Type == VehicleType.Starship).FlightPaths[0].Points;

        foreach (var e in mission.Config.Events.Where(e => e.HasPosition))
        {
            var points = e.Vehicle is { } type ? mission.Vehicles.Single(v => v.Type == type).FlightPaths[0].Points : starship;
            var on = TrajectoryInterpolator.PointAt(points, e.OffsetSeconds);

            Assert.NotNull(on);
            Assert.True(Math.Abs(on.AltitudeMeters - e.AltitudeMeters!.Value) <= 100, $"{e.Type}: altitude");
            var distance = GeodesicCalculator.Inverse(on.Latitude, on.Longitude, e.Latitude!.Value, e.Longitude!.Value).DistanceMeters;
            Assert.True(distance <= 2_000, $"{e.Type}: {distance:F0} m off the path");
        }
    }

    [Fact]
    public async Task DemoProfile_HasExpectedPhases()
    {
        var mission = await LoadDemo();
        var events = mission.Config.Events.ToDictionary(e => e.Type);

        Assert.InRange(events[EventType.MaxQ].OffsetSeconds, 55, 70);
        Assert.InRange(events[EventType.MaxQ].AltitudeMeters!.Value, 10_000, 14_000);
        Assert.InRange(events[EventType.StageSeparation].OffsetSeconds, 150, 170);
        Assert.InRange(events[EventType.StageSeparation].AltitudeMeters!.Value, 60_000, 75_000);
        Assert.InRange(events[EventType.StarshipEngineCutoff].OffsetSeconds, 450, 550);
        Assert.InRange(events[EventType.StarshipEngineCutoff].AltitudeMeters!.Value, 140_000, 160_000);
        Assert.InRange(events[EventType.Reentry].OffsetSeconds, 2_700, 2_900);
        Assert.InRange(events[EventType.SplashdownOrLanding].OffsetSeconds, 3_800, 4_000);
        Assert.InRange(events[EventType.BoosterLanding].OffsetSeconds, 400, 520);
    }
}
