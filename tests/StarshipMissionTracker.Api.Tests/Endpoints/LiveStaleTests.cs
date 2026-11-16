using StarshipMissionTracker.Api.Tests.Support;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Tests.Endpoints;

/// <summary>STALE only while a run of this mission is Running and not paused (plan section 8, N-34).</summary>
public class LiveStaleTests
{
    private static async Task<TestApp> StartAsync(SimulationStatus status, double receivedSecondsAgo, double starshipOffset = 300)
    {
        var app = await TestApp.StartAsync();
        app.Missions.Put(TestData.Info());
        var now = app.Clock.GetUtcNow();
        var received = now.AddSeconds(-receivedSecondsAgo);
        app.Telemetry.Latest.Add(TestData.Sample(VehicleType.Starship, TestData.PlannedT0, starshipOffset, received));
        // Super Heavy has finished: its final sample is at the last planned point (T+400).
        app.Telemetry.Latest.Add(TestData.Sample(VehicleType.SuperHeavy, TestData.PlannedT0, 400, received));
        app.Simulation.Status = status;
        return app;
    }

    private static SimulationStatus Status(SimulationState state, bool paused = false, string mission = TestData.Code) =>
        new(state, mission, 10, paused, TestData.PlannedT0, 300, null);

    private static LiveVehicleState State(TestApp app, VehicleType type) => app.Kml.LiveStates!.Single(s => s.Vehicle.Type == type);

    [Fact]
    public async Task Running_OldSample_IsStale()
    {
        await using var app = await StartAsync(Status(SimulationState.Running), receivedSecondsAgo: 20);

        var body = await app.GetStringAsync("/kml/live?mission=demo-ift");

        var starship = State(app, VehicleType.Starship);
        Assert.True(starship.IsStale);
        Assert.Equal(TimeSpan.FromSeconds(20), starship.Age);
        Assert.Contains("– STALE", body, StringComparison.Ordinal);
        Assert.Equal(app.Clock.GetUtcNow(), app.Kml.LiveNow);
    }

    [Fact]
    public async Task Running_FinishedVehicle_IsNotStale()
    {
        await using var app = await StartAsync(Status(SimulationState.Running), receivedSecondsAgo: 20);

        await app.GetStringAsync("/kml/live?mission=demo-ift");

        Assert.False(State(app, VehicleType.SuperHeavy).IsStale);
    }

    [Fact]
    public async Task Running_FreshSample_IsNotStale()
    {
        await using var app = await StartAsync(Status(SimulationState.Running), receivedSecondsAgo: 10);

        await app.GetStringAsync("/kml/live?mission=demo-ift");

        Assert.False(State(app, VehicleType.Starship).IsStale);
    }

    [Fact]
    public async Task Paused_OldSample_IsNotStale()
    {
        await using var app = await StartAsync(Status(SimulationState.Running, paused: true), receivedSecondsAgo: 600);

        var body = await app.GetStringAsync("/kml/live?mission=demo-ift");

        Assert.False(State(app, VehicleType.Starship).IsStale);
        Assert.DoesNotContain("STALE", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SimulationState.Idle)]
    [InlineData(SimulationState.Countdown)]
    [InlineData(SimulationState.Completed)]
    public async Task NotRunning_OldSample_IsNotStale(SimulationState state)
    {
        await using var app = await StartAsync(Status(state), receivedSecondsAgo: 600);

        await app.GetStringAsync("/kml/live?mission=demo-ift");

        Assert.All(app.Kml.LiveStates!, s => Assert.False(s.IsStale));
    }

    [Fact]
    public async Task RunOfAnotherMission_IsNotStale()
    {
        await using var app = await StartAsync(Status(SimulationState.Running, mission: "other-1"), receivedSecondsAgo: 600);

        await app.GetStringAsync("/kml/live?mission=demo-ift");

        Assert.False(State(app, VehicleType.Starship).IsStale);
    }

    [Fact]
    public async Task StaleThreshold_ComesFromOptions()
    {
        var app = await TestApp.StartAsync(new Dictionary<string, string?> { ["Live:StaleAfterSeconds"] = "30" });
        await using (app)
        {
            app.Missions.Put(TestData.Info());
            app.Telemetry.Latest.Add(TestData.Sample(VehicleType.Starship, TestData.PlannedT0, 300, app.Clock.GetUtcNow().AddSeconds(-20)));
            app.Simulation.Status = Status(SimulationState.Running);

            await app.GetStringAsync("/kml/live?mission=demo-ift");

            Assert.False(State(app, VehicleType.Starship).IsStale);
        }
    }

    [Fact]
    public async Task VehicleWithoutSample_HasNoLatestAndIsNotStale()
    {
        await using var app = await TestApp.StartAsync();
        app.Missions.Put(TestData.Info());
        app.Simulation.Status = Status(SimulationState.Running);

        var body = await app.GetStringAsync("/kml/live?mission=demo-ift");

        Assert.Equal(2, app.Kml.LiveStates!.Count);
        Assert.All(app.Kml.LiveStates, s => Assert.Null(s.Latest));
        Assert.All(app.Kml.LiveStates, s => Assert.False(s.IsStale));
        Assert.Contains("Waiting for liftoff", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_SimulatedSample_IsNeverLabelledMeasured()
    {
        await using var app = await StartAsync(Status(SimulationState.Running), receivedSecondsAgo: 1);

        var body = await app.GetStringAsync("/kml/live?mission=demo-ift");

        Assert.Contains("SIMULATED", body, StringComparison.Ordinal);
        Assert.Contains("not live telemetry", body, StringComparison.Ordinal);
        Assert.DoesNotContain("GPS", body, StringComparison.Ordinal);
    }
}
