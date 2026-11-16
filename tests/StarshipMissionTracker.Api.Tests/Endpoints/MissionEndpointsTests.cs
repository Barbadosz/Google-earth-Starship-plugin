using System.Net;
using System.Text.Json;
using StarshipMissionTracker.Api.Tests.Support;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Tests.Endpoints;

public class MissionEndpointsTests
{
    private const string LaunchPath = "/api/missions/demo-ift/launch-time";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<TestApp> StartWithMissionAsync(MissionInfo? info = null)
    {
        var app = await TestApp.StartAsync();
        app.Missions.Put(info ?? TestData.Info());
        return app;
    }

    [Fact]
    public async Task List_ReturnsMissionsWithStringEnumsAndEffectiveT0()
    {
        var anchor = new DateTimeOffset(2026, 11, 20, 8, 0, 0, 123, TimeSpan.Zero);
        await using var app = await StartWithMissionAsync(TestData.Info(anchor: anchor));

        var json = await JsonAsync(await app.SendAsync(HttpMethod.Get, "/api/missions"));

        var mission = Assert.Single(json.EnumerateArray());
        Assert.Equal("demo-ift", mission.GetProperty("missionCode").GetString());
        Assert.Equal("Scheduled", mission.GetProperty("status").GetString());
        Assert.Equal("2026-11-20T08:00:00.123Z", mission.GetProperty("launchTimeUtc").GetString());
        Assert.Equal("2026-12-01T23:30:00Z", mission.GetProperty("plannedLaunchTimeUtc").GetString());
        Assert.True(mission.GetProperty("isSimulated").GetBoolean());
        Assert.Equal(25.997, mission.GetProperty("launchSite").GetProperty("latitude").GetDouble());
    }

    [Fact]
    public async Task Current_And_ByCode_Return200_Or404()
    {
        await using var app = await TestApp.StartAsync();
        using (var none = await app.SendAsync(HttpMethod.Get, "/api/missions/current"))
        {
            Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        }

        app.Missions.Put(TestData.Info());
        Assert.Equal("demo-ift", (await JsonAsync(await app.SendAsync(HttpMethod.Get, "/api/missions/current"))).GetProperty("missionCode").GetString());
        Assert.Equal("demo-ift", (await JsonAsync(await app.SendAsync(HttpMethod.Get, "/api/missions/demo-ift"))).GetProperty("missionCode").GetString());

        using var unknown = await app.SendAsync(HttpMethod.Get, "/api/missions/nope");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("nope", (await JsonAsync(unknown)).GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/missions/bad%20code")]
    [InlineData("/api/missions/bad%20code/trajectory")]
    [InlineData("/api/missions/bad%20code/events")]
    [InlineData("/api/missions/bad%20code/telemetry")]
    public async Task InvalidCode_Returns400(string path)
    {
        await using var app = await StartWithMissionAsync();

        using var response = await app.SendAsync(HttpMethod.Get, path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Trajectory_PointsCarryQualitySourceAndT0PlusOffset()
    {
        await using var app = await StartWithMissionAsync();

        var json = await JsonAsync(await app.SendAsync(HttpMethod.Get, "/api/missions/demo-ift/trajectory"));

        var paths = json.EnumerateArray().ToList();
        Assert.Equal(["Starship", "SuperHeavy"], paths.Select(p => p.GetProperty("vehicle").GetString()));
        var first = paths[0];
        Assert.Equal("Planned", first.GetProperty("type").GetString());
        Assert.Equal("Simulated", first.GetProperty("quality").GetString());
        var points = first.GetProperty("points").EnumerateArray().ToList();
        Assert.Equal(3, points.Count);
        Assert.All(points, p =>
        {
            Assert.Equal("Simulated", p.GetProperty("quality").GetString());
            Assert.Equal(TestData.Source, p.GetProperty("source").GetString());
        });
        Assert.Equal("2026-12-01T23:40:00Z", points[2].GetProperty("timeUtc").GetString());
    }

    [Fact]
    public async Task Events_OrderedWithTimeFromEffectiveT0()
    {
        var anchor = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        await using var app = await StartWithMissionAsync(TestData.Info(anchor: anchor));

        var json = await JsonAsync(await app.SendAsync(HttpMethod.Get, "/api/missions/demo-ift/events"));

        var events = json.EnumerateArray().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal("StageSeparation", events[1].GetProperty("type").GetString());
        Assert.Equal("2026-11-20T08:02:50Z", events[1].GetProperty("timeUtc").GetString());
        Assert.Equal("Simulated", events[1].GetProperty("quality").GetString());
    }

    [Fact]
    public async Task Telemetry_ParsesQuery_AndReportsProvenance()
    {
        await using var app = await StartWithMissionAsync();
        app.Telemetry.Latest.Add(TestData.Sample(VehicleType.SuperHeavy, TestData.PlannedT0, 12.5, TestData.PlannedT0.AddSeconds(2)));

        using var response = await app.SendAsync(
            HttpMethod.Get, "/api/missions/demo-ift/telemetry?vehicle=superheavy&since=2026-12-01T23:30:00.5Z&limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("demo-ift", (VehicleType?)VehicleType.SuperHeavy, (DateTimeOffset?)TestData.PlannedT0.AddMilliseconds(500), 50), app.Telemetry.LastSamplesQuery);
        var sample = Assert.Single((await JsonAsync(response)).EnumerateArray());
        Assert.Equal("Simulated", sample.GetProperty("quality").GetString());
        Assert.True(sample.GetProperty("isEstimated").GetBoolean());
        Assert.Equal(TestData.SimSource, sample.GetProperty("source").GetString());
        Assert.Equal(12.5, sample.GetProperty("offsetSeconds").GetDouble());
    }

    [Fact]
    public async Task Telemetry_DefaultLimit()
    {
        await using var app = await StartWithMissionAsync();

        await app.SendAsync(HttpMethod.Get, "/api/missions/demo-ift/telemetry");

        Assert.Equal(1000, app.Telemetry.LastSamplesQuery!.Value.Limit);
        Assert.Null(app.Telemetry.LastSamplesQuery.Value.Vehicle);
    }

    [Theory]
    [InlineData("?vehicle=1")]
    [InlineData("?vehicle=Falcon")]
    [InlineData("?since=2026-12-01T23:30:00")]
    [InlineData("?since=2026-12-01T23:30:00%2B00:00")]
    [InlineData("?limit=0")]
    [InlineData("?limit=10001")]
    [InlineData("?limit=-5")]
    [InlineData("?limit=abc")]
    [InlineData("?limit=1&limit=2")]
    public async Task Telemetry_InvalidQuery_Returns400(string query)
    {
        await using var app = await StartWithMissionAsync();

        using var response = await app.SendAsync(HttpMethod.Get, "/api/missions/demo-ift/telemetry" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(app.Telemetry.LastSamplesQuery);
    }

    [Fact]
    public async Task Telemetry_UnknownMission_Returns404()
    {
        await using var app = await StartWithMissionAsync();

        using var response = await app.SendAsync(HttpMethod.Get, "/api/missions/nope/telemetry");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutLaunchTime_Valid_SetsPlannedT0_InvalidatesCache_ReturnsMission()
    {
        await using var app = await StartWithMissionAsync(TestData.Info(anchor: TestData.PlannedT0.AddDays(-3)));
        await app.GetStringAsync("/kml/planned?mission=demo-ift");

        using var response = await app.SendAsync(HttpMethod.Put, LaunchPath, TestApp.Json("{\"launchTimeUtc\":\"2026-12-02T01:15:30.25Z\"}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expected = new DateTimeOffset(2026, 12, 2, 1, 15, 30, 250, TimeSpan.Zero);
        Assert.Equal(("demo-ift", expected), Assert.Single(app.Missions.PlannedEdits));
        var json = await JsonAsync(response);
        Assert.Equal("2026-12-02T01:15:30.25Z", json.GetProperty("plannedLaunchTimeUtc").GetString());
        Assert.Equal("2026-12-02T01:15:30.25Z", json.GetProperty("launchTimeUtc").GetString());

        await app.GetStringAsync("/kml/planned?mission=demo-ift");
        Assert.Equal(2, app.Missions.PlannedLoads);
        Assert.Equal(expected, app.Kml.PlannedMission!.Clock.LaunchTimeUtc);
    }

    [Fact]
    public async Task PutLaunchTime_KeepsSevenFractionDigits()
    {
        await using var app = await StartWithMissionAsync();

        using var response = await app.SendAsync(HttpMethod.Put, LaunchPath, TestApp.Json("{\"launchTimeUtc\":\"2026-12-02T01:15:30.1234567Z\"}"));

        Assert.Equal("2026-12-02T01:15:30.1234567Z", (await JsonAsync(response)).GetProperty("plannedLaunchTimeUtc").GetString());
    }

    [Theory]
    [InlineData(SimulationState.Countdown, false)]
    [InlineData(SimulationState.Running, false)]
    [InlineData(SimulationState.Running, true)]
    public async Task PutLaunchTime_SimulationOfMissionActive_Returns409(SimulationState state, bool paused)
    {
        await using var app = await StartWithMissionAsync();
        app.Simulation.Status = new SimulationStatus(state, "demo-ift", 10, paused, TestData.PlannedT0, 5, null);

        using var response = await app.SendAsync(HttpMethod.Put, LaunchPath, TestApp.Json("{\"launchTimeUtc\":\"2026-12-02T01:15:30Z\"}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("stop it", (await JsonAsync(response)).GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Empty(app.Missions.PlannedEdits);
    }

    [Fact]
    public async Task PutLaunchTime_ConcurrentStart_WaitsUntilTheT0WriteIsDone()
    {
        await using var app = await StartWithMissionAsync();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Simulation.HoldInactive = hold;
        var ct = TestContext.Current.CancellationToken;

        var put = app.SendAsync(HttpMethod.Put, LaunchPath, TestApp.Json("{\"launchTimeUtc\":\"2026-12-02T01:15:30Z\"}"));
        await app.Simulation.InactiveEntered.Task.WaitAsync(ct);
        var start = app.SendAsync(HttpMethod.Post, "/api/simulation/start", TestApp.Json("{\"mission\":\"demo-ift\"}"));
        await app.Simulation.StartRequested.Task.WaitAsync(ct);

        Assert.False(start.IsCompleted); // Start waits for the guarded T0 write
        Assert.Empty(app.Missions.PlannedEdits);
        hold.SetResult();

        using var putResponse = await put;
        using var startResponse = await start;
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Single(app.Missions.PlannedEdits);
        Assert.Equal(["inactive", "inactive-done", "start"], app.Simulation.Calls);
    }

    [Fact]
    public async Task PutLaunchTime_RepositoryFailure_IsNotAConflict()
    {
        await using var app = await StartWithMissionAsync();
        app.Missions.FailNextPlannedEdit = new InvalidOperationException("database is locked");

        var error = await Record.ExceptionAsync(() =>
            app.SendAsync(HttpMethod.Put, LaunchPath, TestApp.Json("{\"launchTimeUtc\":\"2026-12-02T01:15:30Z\"}")));

        // TestServer rethrows an unhandled exception to the client; the real host answers 500. Never 409.
        Assert.IsType<InvalidOperationException>(error);
    }

    [Theory]
    [InlineData(SimulationState.Idle, "demo-ift")]
    [InlineData(SimulationState.Completed, "demo-ift")]
    [InlineData(SimulationState.Running, "other-1")]
    public async Task PutLaunchTime_NoActiveSimulationOfMission_IsAllowed(SimulationState state, string mission)
    {
        await using var app = await StartWithMissionAsync();
        app.Simulation.Status = new SimulationStatus(state, mission, 10, false, TestData.PlannedT0, 5, null);

        using var response = await app.SendAsync(HttpMethod.Put, LaunchPath, TestApp.Json("{\"launchTimeUtc\":\"2026-12-02T01:15:30Z\"}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(app.Missions.PlannedEdits);
    }

    [Theory]
    [InlineData("{\"launchTimeUtc\":\"2026-12-02T01:15:30+00:00\"}")]
    [InlineData("{\"launchTimeUtc\":\"2026-12-02 01:15:30Z\"}")]
    [InlineData("{\"launchTimeUtc\":\"2026-12-02T01:15:30\"}")]
    [InlineData("{\"launchTimeUtc\":\"2026-12-02T01:15:30.Z\"}")]
    [InlineData("{\"launchTimeUtc\":\" 2026-12-02T01:15:30Z\"}")]
    [InlineData("{\"launchTimeUtc\":1764638130}")]
    [InlineData("{\"launchTimeUtc\":null}")]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task PutLaunchTime_InvalidBody_Returns400(string body)
    {
        await using var app = await StartWithMissionAsync();

        using var response = await app.SendRawAsync(HttpMethod.Put, LaunchPath, body, "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Missions.PlannedEdits);
    }

    [Fact]
    public async Task PutLaunchTime_UnknownMission_Returns404()
    {
        await using var app = await StartWithMissionAsync();

        using var response = await app.SendAsync(HttpMethod.Put, "/api/missions/nope/launch-time", TestApp.Json("{\"launchTimeUtc\":\"2026-12-02T01:15:30Z\"}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutLaunchTime_NonLoopback_Returns403()
    {
        await using var app = await StartWithMissionAsync();

        using var response = await app.SendAsync(
            HttpMethod.Put, LaunchPath, TestApp.Json("{\"launchTimeUtc\":\"2026-12-02T01:15:30Z\"}"), remoteIp: "203.0.113.9");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(app.Missions.PlannedEdits);
    }

    [Fact]
    public async Task PutLaunchTime_WithoutJsonContentType_Returns415()
    {
        await using var app = await StartWithMissionAsync();

        using var response = await app.SendRawAsync(HttpMethod.Put, LaunchPath, "{\"launchTimeUtc\":\"2026-12-02T01:15:30Z\"}", "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(app.Missions.PlannedEdits);
    }
}
