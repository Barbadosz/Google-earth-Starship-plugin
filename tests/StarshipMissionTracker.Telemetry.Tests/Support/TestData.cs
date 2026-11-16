using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Simulation;

namespace StarshipMissionTracker.Telemetry.Tests.Support;

public static class TestData
{
    public static readonly DateTimeOffset Start = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static TrajectoryPoint P(double t, double lat, double lon, double alt, double? v = null) =>
        new(t, lat, lon, alt, v, DataQuality.Estimated, "test");

    /// <summary>Starship: 0..20 s; Super Heavy: 0..8 s (lands earlier). No headings, so orientation is derived.</summary>
    public static Mission Mission(string code = "demo")
    {
        var ship = new FlightPath(TrajectoryType.Planned, DataQuality.Estimated, "test-plan", null,
            [P(0, 25.0, -97.0, 0), P(10, 25.0, -96.9, 5000, 700), P(20, 25.0, -96.7, 20000)]);
        var booster = new FlightPath(TrajectoryType.Planned, DataQuality.Estimated, "test-plan", null,
            [P(0, 25.0, -97.0, 0), P(8, 25.0, -96.95, 3000)]);
        var config = new MissionConfig(code, "Demo", Start, null, []);
        return new Mission(
            config,
            new LaunchClock(Start),
            [new Vehicle("Ship", VehicleType.Starship, null, [ship]), new Vehicle("Booster", VehicleType.SuperHeavy, null, [booster])]);
    }

    public sealed class Rig
    {
        public Rig(double intervalSeconds = 1, int maxFailures = 3, bool simulated = true)
        {
            Time = new ManualTimeProvider(TestData.Start);
            Missions = new FakeMissionRepository { Mission = TestData.Mission(), IsSimulated = simulated };
            Telemetry = new FakeTelemetryRepository();
            Missions.LinkedTelemetry = Telemetry;
            Options = new SimulationOptions { SampleIntervalSeconds = intervalSeconds, MaxConsecutiveFailures = maxFailures };
            Controller = new SimulationController(
                new FakeScopeFactory(Missions, Telemetry), Time, Options, Logger);
        }

        public CollectingLogger<SimulationController> Logger { get; } = new();
        public ManualTimeProvider Time { get; }
        public FakeMissionRepository Missions { get; }
        public FakeTelemetryRepository Telemetry { get; }
        public SimulationOptions Options { get; }
        public SimulationController Controller { get; }

        public Task<SimulationStatus> Start(double speed = 1, double countdown = 10) =>
            Controller.StartAsync(new SimulationStartRequest("demo", speed, countdown), TestContext.Current.CancellationToken);

        /// <summary>Advances the wall clock in small steps, ticking after each (jitter-free).</summary>
        public async Task Run(double wallSeconds, double stepSeconds = 0.25)
        {
            var steps = (int)Math.Round(wallSeconds / stepSeconds);
            for (var i = 0; i < steps; i++)
            {
                Time.Advance(TimeSpan.FromSeconds(stepSeconds));
                await Controller.TickAsync(TestContext.Current.CancellationToken);
            }
        }
    }
}
