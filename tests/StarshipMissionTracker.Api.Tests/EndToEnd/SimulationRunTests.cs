using System.Net;
using System.Text.Json;

namespace StarshipMissionTracker.Api.Tests.EndToEnd;

/// <summary>
/// N-31, part 1: a simulated run on the real host with a manual clock. Mission time = wall time x speed after the
/// countdown; one tick per <see cref="EndToEndHost.AdvanceAsync"/>. No real waiting.
/// </summary>
public class SimulationRunTests
{
    private const double Tolerance = 1e-3;

    private static double Offset(JsonElement status) => status.GetProperty("missionOffsetSeconds").GetDouble();

    [Fact]
    public async Task Run_HistoryGrows_LiveMoves_AndEverythingIsMarkedSimulated()
    {
        await using var host = EndToEndHost.Start();

        var started = await host.StartSimulationAsync(speed: 10, countdown: 10);
        Assert.Equal("Countdown", started.GetProperty("state").GetString());
        Assert.Equal(-10, Offset(started), Tolerance);
        Assert.Equal("Simulated", started.GetProperty("dataQuality").GetString());
        Assert.Equal(0, EndToEndHost.PointCount(await host.KmlAsync("history")));
        // N-49: countdown -> the model stands at the planned pad position, labelled as planned, with no telemetry sample.
        var countdownLive = await host.KmlAsync("live");
        Assert.NotNull(EndToEndHost.LiveLongitude(countdownLive, "Starship"));
        Assert.Contains("countdown, planned pad position", countdownLive.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SampleTimeUtc", countdownLive.ToString(), StringComparison.Ordinal);

        await host.AdvanceAsync(10);
        await host.AdvanceAsync(5);
        var history1 = await host.KmlAsync("history");
        var live1 = await host.KmlAsync("live");
        var points1 = EndToEndHost.PointCount(history1);
        var lon1 = EndToEndHost.LiveLongitude(live1, "Starship");

        await host.AdvanceAsync(20);
        var history2 = await host.KmlAsync("history");
        var live2 = await host.KmlAsync("live");
        var points2 = EndToEndHost.PointCount(history2);
        var lon2 = EndToEndHost.LiveLongitude(live2, "Starship");

        Assert.True(points1 > 0, "history has points after liftoff");
        Assert.True(points2 > points1, $"history grows over time ({points1} -> {points2})");
        Assert.NotNull(lon1);
        Assert.NotNull(lon2);
        Assert.True(lon2 > lon1, $"the live position moves downrange ({lon1} -> {lon2})");

        var status = await host.StatusAsync();
        Assert.Equal("Running", status.GetProperty("state").GetString());
        Assert.Equal(250, Offset(status), Tolerance);

        // Provenance: every document and sample says "simulated", never measured.
        var liveText = live2.ToString();
        Assert.Contains("(SIMULATED)", liveText, StringComparison.Ordinal);
        Assert.Contains("Simulated position – not live telemetry.", liveText, StringComparison.Ordinal);
        var qualities = live2.Descendants(EndToEndHost.K("Data")).Where(d => (string?)d.Attribute("name") == "DataQuality").ToList();
        Assert.NotEmpty(qualities);
        Assert.All(qualities, d => Assert.Equal("Simulated", d.Element(EndToEndHost.K("value"))!.Value));
        Assert.Contains("SIMULATED", history2.ToString(), StringComparison.Ordinal);
        foreach (var vehicle in new[] { "Starship", "SuperHeavy" })
        {
            var samples = await host.SamplesAsync(vehicle);
            Assert.NotEmpty(samples);
            Assert.All(samples, s =>
            {
                Assert.Equal("Simulated", s.GetProperty("quality").GetString());
                Assert.True(s.GetProperty("isEstimated").GetBoolean());
                Assert.Equal("Internal simulator", s.GetProperty("source").GetString());
            });
        }
    }

    [Fact]
    public async Task SpeedChanges_WhileRunning_KeepMissionTimeContinuous()
    {
        await using var host = EndToEndHost.Start();
        await host.StartSimulationAsync(speed: 10, countdown: 10);
        await host.AdvanceAsync(10);
        await host.AdvanceAsync(10);
        Assert.Equal(100, Offset(await host.StatusAsync()), Tolerance);

        // 10x -> 100x: the offset right after the change is the offset right before it (no jump), then 100 s/s.
        var up = await host.SendJsonAsync(HttpMethod.Put, "/api/simulation/speed", "{\"speed\":100}");
        Assert.Equal(100, Offset(up), Tolerance);
        Assert.Equal(100, up.GetProperty("speed").GetDouble(), Tolerance);
        await host.AdvanceAsync(2);
        Assert.Equal(300, Offset(await host.StatusAsync()), Tolerance);

        // 100x -> 0.1x.
        var down = await host.SendJsonAsync(HttpMethod.Put, "/api/simulation/speed", "{\"speed\":0.1}");
        Assert.Equal(300, Offset(down), Tolerance);
        await host.AdvanceAsync(10);
        Assert.Equal(301, Offset(await host.StatusAsync()), Tolerance);

        // The stored samples lie on one gap-free axis: every mission second once, whatever the speed was.
        foreach (var vehicle in new[] { "Starship", "SuperHeavy" })
        {
            var offsets = (await host.SamplesAsync(vehicle)).Select(s => s.GetProperty("offsetSeconds").GetDouble()).ToList();
            Assert.Equal(302, offsets.Count);
            for (var i = 0; i < offsets.Count; i++)
            {
                Assert.Equal(i, offsets[i], Tolerance);
            }
        }
    }

    [Theory]
    [InlineData("0.09")]
    [InlineData("100.5")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task SpeedOutsideTheRange_IsRejected_AndTheRunKeepsItsSpeed(string speed)
    {
        await using var host = EndToEndHost.Start();
        await host.StartSimulationAsync(speed: 10, countdown: 10);
        await host.AdvanceAsync(15);

        using var response = await host.SendAsync(HttpMethod.Put, "/api/simulation/speed", "{\"speed\":" + speed + "}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var status = await host.StatusAsync();
        Assert.Equal(10, status.GetProperty("speed").GetDouble(), Tolerance);
        Assert.Equal(50, Offset(status), Tolerance);
    }

    [Fact]
    public async Task PauseAndResume_FreezeTheMissionClock_AndStaleOnlyWhileRunning()
    {
        await using var host = EndToEndHost.Start();
        await host.StartSimulationAsync(speed: 10, countdown: 10);
        await host.AdvanceAsync(10);
        await host.AdvanceAsync(5);
        Assert.DoesNotContain("STALE", (await host.KmlAsync("live")).ToString(), StringComparison.Ordinal);

        // Positive control: the tick loop stalls for 20 s while running -> the latest sample is older than the 15 s limit.
        host.Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Contains("STALE", (await host.KmlAsync("live")).ToString(), StringComparison.Ordinal);

        using var pause = await host.PostAsync("/api/simulation/pause");
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        var pausedStatus = await host.StatusAsync();
        Assert.True(pausedStatus.GetProperty("paused").GetBoolean());
        Assert.Equal("Running", pausedStatus.GetProperty("state").GetString());
        Assert.Equal(250, Offset(pausedStatus), Tolerance);
        Assert.DoesNotContain("STALE", (await host.KmlAsync("live")).ToString(), StringComparison.Ordinal);

        // One tick stores the samples up to the pause point; after that a long pause changes nothing.
        await host.Controller.TickAsync(EndToEndHost.Ct);
        var storedAtPause = (await host.SamplesAsync()).Count;
        await host.AdvanceAsync(600);
        await host.AdvanceAsync(600);
        Assert.Equal(storedAtPause, (await host.SamplesAsync()).Count);
        Assert.Equal(250, Offset(await host.StatusAsync()), Tolerance);
        Assert.DoesNotContain("STALE", (await host.KmlAsync("live")).ToString(), StringComparison.Ordinal);

        using var pausedAgain = await host.PostAsync("/api/simulation/pause");
        Assert.Equal(HttpStatusCode.Conflict, pausedAgain.StatusCode);

        var resumed = await host.SendJsonAsync(HttpMethod.Post, "/api/simulation/resume", "{}");
        Assert.False(resumed.GetProperty("paused").GetBoolean());
        Assert.Equal(250, Offset(resumed), Tolerance);
        await host.AdvanceAsync(1);
        Assert.Equal(260, Offset(await host.StatusAsync()), Tolerance);
        Assert.True((await host.SamplesAsync()).Count > storedAtPause);
        Assert.DoesNotContain("STALE", (await host.KmlAsync("live")).ToString(), StringComparison.Ordinal);

        using var resumedAgain = await host.PostAsync("/api/simulation/resume");
        Assert.Equal(HttpStatusCode.Conflict, resumedAgain.StatusCode);
    }

    [Fact]
    public async Task Restart_AfterStop_IsAFreshRun_AndOldSimulatedSamplesAreCleared()
    {
        await using var host = EndToEndHost.Start();
        await host.StartSimulationAsync(speed: 10, countdown: 10);
        await host.AdvanceAsync(10);
        await host.AdvanceAsync(10);

        using var second = await host.PostAsync("/api/simulation/start", "{\"mission\":\"demo-ift\"}");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var stopped = await host.SendJsonAsync(HttpMethod.Post, "/api/simulation/stop", "{}");
        Assert.Equal("Idle", stopped.GetProperty("state").GetString());
        using var stoppedAgain = await host.PostAsync("/api/simulation/stop");
        Assert.Equal(HttpStatusCode.Conflict, stoppedAgain.StatusCode);

        // The stopped run's samples stay readable (N-37) until the next start.
        Assert.True((await host.SamplesAsync()).Count > 90);
        Assert.True(EndToEndHost.PointCount(await host.KmlAsync("history")) > 0);
        var oldAnchor = (await host.MissionAsync()).GetProperty("simulationLaunchTimeUtc").GetString();
        Assert.NotNull(oldAnchor);

        host.Clock.Advance(TimeSpan.FromSeconds(1000));
        var restarted = await host.StartSimulationAsync(speed: 10, countdown: 5);
        Assert.Equal("Countdown", restarted.GetProperty("state").GetString());
        Assert.Equal(-5, Offset(restarted), Tolerance);
        var mission = await host.MissionAsync();
        var newAnchor = mission.GetProperty("simulationLaunchTimeUtc").GetString();
        Assert.NotEqual(oldAnchor, newAnchor);
        Assert.Equal(newAnchor, restarted.GetProperty("launchTimeUtc").GetString());
        Assert.Equal(newAnchor, mission.GetProperty("launchTimeUtc").GetString());
        Assert.Empty(await host.SamplesAsync());
        Assert.Equal(0, EndToEndHost.PointCount(await host.KmlAsync("history")));

        await host.AdvanceAsync(5);
        await host.AdvanceAsync(3);
        var offsets = (await host.SamplesAsync()).Select(s => s.GetProperty("offsetSeconds").GetDouble()).ToList();
        Assert.Equal(31, offsets.Count);
        Assert.Equal(0, offsets[0], Tolerance);
        Assert.Equal(30, offsets[^1], Tolerance);
    }

    [Fact]
    public async Task Start_WithOnlyTheMission_UsesTheDefaults_AndTicksWithoutARunAreHarmless()
    {
        await using var host = EndToEndHost.Start();

        await host.AdvanceAsync(60);
        var idle = await host.StatusAsync();
        Assert.Equal("Idle", idle.GetProperty("state").GetString());
        Assert.Empty(await host.SamplesAsync());

        using var response = await host.PostAsync("/api/simulation/start", "{\"mission\":\"demo-ift\"}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var started = JsonDocument.Parse(await response.Content.ReadAsStringAsync(EndToEndHost.Ct)).RootElement;
        Assert.Equal(10, started.GetProperty("speed").GetDouble(), Tolerance);
        Assert.Equal(-10, Offset(started), Tolerance);
    }
}
