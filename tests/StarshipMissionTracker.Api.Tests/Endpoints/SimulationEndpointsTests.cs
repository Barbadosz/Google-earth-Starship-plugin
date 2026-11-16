using System.Net;
using System.Text.Json;
using StarshipMissionTracker.Api.Tests.Support;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Tests.Endpoints;

public class SimulationEndpointsTests
{
    private static readonly SimulationStatus RunningStatus =
        new(SimulationState.Running, TestData.Code, 10, false, TestData.PlannedT0, 123.5, null);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var json = await JsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task GetStatus_HasControlPageContractFields()
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.Status = RunningStatus with { IsPaused = true, Reason = "x" };

        using var response = await app.SendAsync(HttpMethod.Get, "/api/simulation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.Equal("Running", json.GetProperty("state").GetString());
        Assert.Equal(TestData.Code, json.GetProperty("mission").GetString());
        Assert.Equal(10, json.GetProperty("speed").GetDouble());
        Assert.True(json.GetProperty("paused").GetBoolean());
        Assert.Equal(123.5, json.GetProperty("missionOffsetSeconds").GetDouble());
        Assert.Equal("2026-12-01T23:30:00Z", json.GetProperty("launchTimeUtc").GetString());
        Assert.Equal("x", json.GetProperty("reason").GetString());
        Assert.Equal(SimulationSpeedLimits.Min, json.GetProperty("speedMin").GetDouble());
        Assert.Equal(SimulationSpeedLimits.Max, json.GetProperty("speedMax").GetDouble());
        Assert.Equal("Simulated", json.GetProperty("dataQuality").GetString());
    }

    [Fact]
    public async Task GetStatus_Idle_HasNullOffsetAndLaunchTime()
    {
        await using var app = await TestApp.StartAsync();

        var json = await JsonAsync(await app.SendAsync(HttpMethod.Get, "/api/simulation"));

        Assert.Equal("Idle", json.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("missionOffsetSeconds").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("launchTimeUtc").ValueKind);
    }

    [Theory]
    [InlineData("2.5", 2.5)]
    [InlineData("0.1", 0.1)]
    [InlineData("100", 100)]
    [InlineData("1e1", 10)]
    public async Task PutSpeed_Valid_Returns200WithNewStatus(string value, double expected)
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.Status = RunningStatus;

        using var response = await app.SendAsync(HttpMethod.Put, "/api/simulation/speed", TestApp.Json($"{{\"speed\":{value}}}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, app.Simulation.LastSpeed);
        Assert.Equal(expected, (await JsonAsync(response)).GetProperty("speed").GetDouble());
    }

    [Theory]
    [InlineData("{\"speed\":0}")]
    [InlineData("{\"speed\":-1}")]
    [InlineData("{\"speed\":0.0999}")]
    [InlineData("{\"speed\":100.01}")]
    [InlineData("{\"speed\":1e400}")]
    [InlineData("{\"speed\":NaN}")]
    [InlineData("{\"speed\":\"NaN\"}")]
    [InlineData("{\"speed\":\"10\"}")]
    [InlineData("{\"speed\":null}")]
    [InlineData("{\"speed\":true}")]
    [InlineData("{}")]
    [InlineData("{\"Speed\":10}")]
    [InlineData("{\"speed\":1,\"speed\":2}")]
    [InlineData("[10]")]
    [InlineData("10")]
    [InlineData("")]
    [InlineData("{\"speed\":")]
    public async Task PutSpeed_Invalid_Returns400WithReason_AndDoesNotCallController(string body)
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.Status = RunningStatus;

        using var response = await app.SendRawAsync(HttpMethod.Put, "/api/simulation/speed", body, "application/json");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Empty(app.Simulation.Calls);
    }

    [Fact]
    public async Task PutSpeed_TooLargeBody_Returns413()
    {
        await using var app = await TestApp.StartAsync();

        using var response = await app.SendRawAsync(
            HttpMethod.Put, "/api/simulation/speed", "{\"speed\":10,\"x\":\"" + new string('a', 20_000) + "\"}", "application/json");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PutSpeed_WrongState_Returns409WithControllerReason()
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.NextError = new SimulationStateException("Speed change rejected: the simulation is Idle");

        using var response = await app.SendAsync(HttpMethod.Put, "/api/simulation/speed", TestApp.Json("{\"speed\":2}"));

        await AssertErrorAsync(response, HttpStatusCode.Conflict);
        Assert.Contains("Idle", (await JsonAsync(response)).GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutSpeed_ControllerRejectsArgument_Returns400()
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.NextError = new ArgumentOutOfRangeException("speed", 5.0, "Speed must be finite.");

        using var response = await app.SendAsync(HttpMethod.Put, "/api/simulation/speed", TestApp.Json("{\"speed\":5}"));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("Speed must be finite.", (await JsonAsync(response)).GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("POST", "/api/simulation/start")]
    [InlineData("POST", "/api/simulation/stop")]
    [InlineData("PUT", "/api/simulation/speed")]
    [InlineData("POST", "/api/simulation/pause")]
    [InlineData("POST", "/api/simulation/resume")]
    public async Task Mutating_NonLoopback_Returns403(string method, string path)
    {
        await using var app = await TestApp.StartAsync();

        using var response = await app.SendAsync(new HttpMethod(method), path, TestApp.Json("{\"speed\":2,\"mission\":\"demo-ift\"}"), remoteIp: "192.168.1.20");

        await AssertErrorAsync(response, HttpStatusCode.Forbidden);
        Assert.Empty(app.Simulation.Calls);
    }

    [Fact]
    public async Task Mutating_UnknownRemoteAddress_Returns403()
    {
        await using var app = await TestApp.StartAsync();

        using var response = await app.SendAsync(HttpMethod.Post, "/api/simulation/stop", TestApp.Json("{}"), remoteIp: "none");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("127.0.0.2")]
    public async Task Mutating_LoopbackVariants_AreAllowed(string ip)
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.Status = RunningStatus;

        using var response = await app.SendAsync(HttpMethod.Post, "/api/simulation/pause", TestApp.Json("{}"), remoteIp: ip);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("application/json; charset=latin1")]
    [InlineData("application/merge-patch+json")]
    public async Task Mutating_WithoutJsonContentType_Returns415(string? contentType)
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.Status = RunningStatus;

        using var response = await app.SendRawAsync(HttpMethod.Put, "/api/simulation/speed", "{\"speed\":2}", contentType);

        await AssertErrorAsync(response, HttpStatusCode.UnsupportedMediaType);
        Assert.Empty(app.Simulation.Calls);
    }

    [Fact]
    public async Task Mutating_JsonWithUtf8Charset_IsAccepted()
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.Status = RunningStatus;

        using var response = await app.SendRawAsync(HttpMethod.Put, "/api/simulation/speed", "{\"speed\":2}", "application/json; charset=utf-8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Mutating_NonLoopbackAndWrongContentType_LoopbackCheckComesFirst()
    {
        await using var app = await TestApp.StartAsync();
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "text/plain");

        using var response = await app.SendAsync(HttpMethod.Post, "/api/simulation/stop", content, remoteIp: "10.0.0.5");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Mutating_ResponsesHaveNoCorsHeaders()
    {
        await using var app = await TestApp.StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/simulation/speed");
        request.Headers.Add("Origin", "http://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "PUT");

        using var response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Empty(app.Simulation.Calls);
    }

    [Fact]
    public async Task PauseResume_Return200WithStatus()
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.Status = RunningStatus;

        var paused = await JsonAsync(await app.SendAsync(HttpMethod.Post, "/api/simulation/pause", TestApp.Json("{}")));
        var resumed = await JsonAsync(await app.SendAsync(HttpMethod.Post, "/api/simulation/resume", TestApp.Json("{}")));

        Assert.True(paused.GetProperty("paused").GetBoolean());
        Assert.False(resumed.GetProperty("paused").GetBoolean());
        Assert.Equal(["pause", "resume"], app.Simulation.Calls);
    }

    [Theory]
    [InlineData("/api/simulation/pause")]
    [InlineData("/api/simulation/resume")]
    [InlineData("/api/simulation/stop")]
    public async Task PauseResumeStop_WrongState_Returns409(string path)
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.NextError = new SimulationStateException("Pause rejected: the simulation is Countdown");

        using var response = await app.SendAsync(HttpMethod.Post, path, TestApp.Json("{}"));

        await AssertErrorAsync(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Stop_Returns200()
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.Status = RunningStatus;

        var json = await JsonAsync(await app.SendAsync(HttpMethod.Post, "/api/simulation/stop", TestApp.Json("{}")));

        Assert.Equal("Idle", json.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Start_ValidBody_PassesRequestAndReturnsStatus()
    {
        await using var app = await TestApp.StartAsync();

        using var response = await app.SendAsync(
            HttpMethod.Post, "/api/simulation/start", TestApp.Json("{\"mission\":\"demo-ift\",\"speed\":0.5,\"countdownSeconds\":30}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new SimulationStartRequest("demo-ift", 0.5, 30), app.Simulation.LastStart);
        var json = await JsonAsync(response);
        Assert.Equal("Countdown", json.GetProperty("state").GetString());
        Assert.Equal(-30, json.GetProperty("missionOffsetSeconds").GetDouble());
    }

    [Fact]
    public async Task Start_DefaultsSpeedFromOptionsAndCountdown()
    {
        await using var app = await TestApp.StartAsync(new Dictionary<string, string?> { ["Simulation:DefaultSpeed"] = "5" });

        using var response = await app.SendAsync(HttpMethod.Post, "/api/simulation/start", TestApp.Json("{\"mission\":\"demo-ift\"}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, app.Simulation.LastStart!.CountdownSeconds);
        Assert.Equal(5, app.Simulation.LastStart.Speed);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"mission\":\"\"}")]
    [InlineData("{\"mission\":\"bad code\"}")]
    [InlineData("{\"mission\":42}")]
    [InlineData("{\"mission\":\"demo-ift\",\"speed\":0}")]
    [InlineData("{\"mission\":\"demo-ift\",\"speed\":\"fast\"}")]
    [InlineData("{\"mission\":\"demo-ift\",\"countdownSeconds\":-1}")]
    [InlineData("{\"mission\":\"demo-ift\",\"countdownSeconds\":1e12}")]
    public async Task Start_InvalidBody_Returns400(string body)
    {
        await using var app = await TestApp.StartAsync();

        using var response = await app.SendRawAsync(HttpMethod.Post, "/api/simulation/start", body, "application/json");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Empty(app.Simulation.Calls);
    }

    [Fact]
    public async Task Start_UnknownMission_Returns404()
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.NextError = new KeyNotFoundException("Unknown mission 'x1'.");

        using var response = await app.SendAsync(HttpMethod.Post, "/api/simulation/start", TestApp.Json("{\"mission\":\"x1\"}"));

        await AssertErrorAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Start_NotSimulatedOrAlreadyRunning_Returns409()
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.NextError = new SimulationStateException("Start rejected: mission 'x1' is not a simulated mission");

        using var response = await app.SendAsync(HttpMethod.Post, "/api/simulation/start", TestApp.Json("{\"mission\":\"x1\"}"));

        await AssertErrorAsync(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InternalInvalidOperation_IsNotMappedTo409()
    {
        await using var app = await TestApp.StartAsync();
        app.Simulation.NextError = new InvalidOperationException("No planned point at offset 3 s for Starship.");

        var error = await Record.ExceptionAsync(() =>
            app.SendAsync(HttpMethod.Post, "/api/simulation/start", TestApp.Json("{\"mission\":\"x1\"}")));

        // TestServer rethrows an unhandled exception to the client; the real host answers 500 without the message.
        Assert.IsType<InvalidOperationException>(error);
    }
}
