using System.Net;
using System.Text.Json;

namespace StarshipMissionTracker.Api.Tests.Attack;

/// <summary>M2 attack: simulation lifecycle and clock on the real host with a manual clock (no real waiting).</summary>
public class M2SimulationAttackTests
{
    private static void AssertOrderedAndUnique(List<JsonElement> samples, string what)
    {
        var offsets = samples.Select(s => AttackHost.Num(s, "offsetSeconds")).ToList();
        for (var i = 1; i < offsets.Count; i++)
        {
            Assert.True(offsets[i] > offsets[i - 1], $"{what}: offsets not strictly increasing at {i}: {offsets[i - 1]} then {offsets[i]}");
        }

        var stamps = samples.Select(s => s.GetProperty("timestampUtc").GetString()).ToList();
        Assert.Equal(stamps.Count, stamps.Distinct().Count());
    }

    [Fact]
    public async Task MaxSpeed_OneHugeTick_CompletesWithoutGapsOrDuplicates_AndLaterTicksAreNoOps()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 100, countdown: 0);

        await host.AdvanceAsync(86_400);

        var status = await host.StatusAsync();
        Assert.Equal("Completed", AttackHost.State(status));
        var starship = await host.SamplesAsync("Starship");
        var booster = await host.SamplesAsync("SuperHeavy");
        AssertOrderedAndUnique(starship, "Starship");
        AssertOrderedAndUnique(booster, "SuperHeavy");
        Assert.Equal(3900, AttackHost.Num(starship[^1], "offsetSeconds"), 1e-6);
        Assert.Equal(520, AttackHost.Num(booster[^1], "offsetSeconds"), 1e-6);
        Assert.Equal(0, AttackHost.Num(starship[0], "offsetSeconds"), 1e-6);
        var frozen = AttackHost.Offset(status);

        for (var i = 0; i < 5; i++)
        {
            await host.AdvanceAsync(3_600);
        }

        Assert.Equal(starship.Count, (await host.SamplesAsync("Starship")).Count);
        Assert.Equal(booster.Count, (await host.SamplesAsync("SuperHeavy")).Count);
        Assert.Equal(frozen, AttackHost.Offset(await host.StatusAsync()));
        Assert.Equal("Completed", (await host.MissionAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Completed_RejectsPauseResumeSpeed_StopKeepsResult_SecondStopIs409_RestartReplacesSamples()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 100, countdown: 0);
        await host.AdvanceAsync(86_400);
        var count = (await host.SamplesAsync("Starship")).Count;

        foreach (var (method, path, body) in new[]
                 {
                     (HttpMethod.Post, "/api/simulation/pause", "{}"),
                     (HttpMethod.Post, "/api/simulation/resume", "{}"),
                     (HttpMethod.Put, "/api/simulation/speed", "{\"speed\":5}"),
                     (HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"demo-ift\",\"countdownSeconds\":0,\"speed\":-1}"),
                 })
        {
            var (code, text) = await host.TextAsync(method, path, body);
            Assert.True(code is HttpStatusCode.Conflict or HttpStatusCode.BadRequest, $"{path} while Completed gave {(int)code}: {text}");
        }

        var stopped = await host.PostAsync("/api/simulation/stop");
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        Assert.Equal("Completed", (await host.MissionAsync()).GetProperty("status").GetString());
        Assert.Equal(count, (await host.SamplesAsync("Starship")).Count);
        var second = await host.PostAsync("/api/simulation/stop");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var restarted = await host.StartAsync(speed: 10, countdown: 60);
        Assert.Equal("Countdown", AttackHost.State(restarted));
        Assert.Empty(await host.SamplesAsync("Starship"));
        Assert.Equal("Scheduled", (await host.MissionAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task LongPause_ThreeDays_ThenResume_DoesNotCatchUp_AndOffsetIsContinuous()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(30);
        var pausedStatus = await host.JsonAsync(HttpMethod.Post, "/api/simulation/pause", "{}");
        var frozen = AttackHost.Offset(pausedStatus);
        await host.AdvanceAsync(0);
        var before = (await host.SamplesAsync("Starship")).Count;

        for (var day = 0; day < 3; day++)
        {
            await host.AdvanceAsync(86_400);
        }

        Assert.Equal(frozen, AttackHost.Offset(await host.StatusAsync()), 1e-6);
        Assert.Equal(before, (await host.SamplesAsync("Starship")).Count);
        var resumed = await host.JsonAsync(HttpMethod.Post, "/api/simulation/resume", "{}");
        Assert.Equal(frozen, AttackHost.Offset(resumed), 1e-6);
        await host.AdvanceAsync(2);
        Assert.Equal(frozen + 20, AttackHost.Offset(await host.StatusAsync()), 1e-6);
        var samples = await host.SamplesAsync("Starship");
        AssertOrderedAndUnique(samples, "after long pause");
        Assert.True(samples.Count <= before + 25, $"samples jumped from {before} to {samples.Count} after resume");
        var live = await host.KmlTextAsync("live");
        Assert.DoesNotContain("STALE", live, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpeedChangeWhilePaused_AppliesAfterResume_WithoutAJump()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(10);
        var paused = await host.JsonAsync(HttpMethod.Post, "/api/simulation/pause", "{}");
        var frozen = AttackHost.Offset(paused);
        await host.AdvanceAsync(600);

        var changed = await host.JsonAsync(HttpMethod.Put, "/api/simulation/speed", "{\"speed\":100}");
        Assert.True(changed.GetProperty("paused").GetBoolean());
        Assert.Equal(frozen, AttackHost.Offset(changed), 1e-6);
        await host.AdvanceAsync(600);
        Assert.Equal(frozen, AttackHost.Offset(await host.StatusAsync()), 1e-6);

        await host.PostAsync("/api/simulation/resume");
        await host.AdvanceAsync(1);

        Assert.Equal(frozen + 100, AttackHost.Offset(await host.StatusAsync()), 1e-6);
    }

    [Fact]
    public async Task WallClockJumpsBackwards_WhileRunning_NeverDuplicatesOrCrashes()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(30);
        var before = await host.SamplesAsync("Starship");

        await host.AdvanceAsync(-3_600);
        await host.AdvanceAsync(-3_600);
        await host.AdvanceAsync(0);
        var status = await host.StatusAsync();
        AttackHost.AssertSaneKml(await host.KmlAsync("live"), "live while the clock is behind");
        AttackHost.AssertSaneKml(await host.KmlAsync("history"), "history while the clock is behind");
        await host.AdvanceAsync(7_200 + 40);

        var after = await host.SamplesAsync("Starship");
        AssertOrderedAndUnique(after, "after the clock went back and returned");
        Assert.True(after.Count >= before.Count);
        Assert.True(double.IsFinite(AttackHost.Offset(status)));
        Assert.Contains(AttackHost.State(await host.StatusAsync()), new[] { "Running", "Completed" });
    }

    [Fact]
    public async Task TicksAtTheSameInstant_NeverStoreTheSameSampleTwice()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(5);

        for (var i = 0; i < 25; i++)
        {
            await host.AdvanceAsync(0);
        }

        AssertOrderedAndUnique(await host.SamplesAsync("Starship"), "Starship");
        AssertOrderedAndUnique(await host.SamplesAsync("SuperHeavy"), "SuperHeavy");
    }

    [Fact]
    public async Task CountdownZero_IsRunningAtOnce_AndPauseRightAwayIsAllowed()
    {
        await using var host = AttackHost.Start();

        var started = await host.StartAsync(speed: 1, countdown: 0);
        Assert.Equal("Running", AttackHost.State(started));
        Assert.Equal(0, AttackHost.Offset(started), 1e-9);

        var paused = await host.JsonAsync(HttpMethod.Post, "/api/simulation/pause", "{}");
        Assert.True(paused.GetProperty("paused").GetBoolean());
        await host.AdvanceAsync(100);
        AssertOrderedAndUnique(await host.SamplesAsync("Starship"), "paused at T0");
        Assert.Equal(0, AttackHost.Offset(await host.StatusAsync()), 1e-9);
    }

    [Theory]
    [InlineData(86_400)]
    [InlineData(0.0000001)]
    public async Task CountdownExtremes_AreAccepted_AndStopCleansUp(double countdown)
    {
        await using var host = AttackHost.Start();

        var started = await host.StartAsync(speed: 1, countdown: countdown);

        Assert.True(AttackHost.State(started) is "Countdown" or "Running");
        var stopped = await host.PostAsync("/api/simulation/stop");
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        Assert.Equal("Idle", AttackHost.State(await host.StatusAsync()));
        Assert.Equal("Scheduled", (await host.MissionAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task StopInCountdown_ClearsTheAnchor_StopMidRunKeepsIt_T0PutResetsBoth()
    {
        await using var host = AttackHost.Start();
        var planned = (await host.MissionAsync()).GetProperty("plannedLaunchTimeUtc").GetString();

        await host.StartAsync(speed: 10, countdown: 120);
        Assert.NotEqual(planned, (await host.MissionAsync()).GetProperty("launchTimeUtc").GetString());
        await host.PostAsync("/api/simulation/stop");
        var afterCountdownStop = await host.MissionAsync();
        Assert.Equal(planned, afterCountdownStop.GetProperty("launchTimeUtc").GetString());
        Assert.Equal(JsonValueKind.Null, afterCountdownStop.GetProperty("simulationLaunchTimeUtc").ValueKind);

        await host.StartAsync(speed: 10, countdown: 5);
        await host.AdvanceAsync(20);
        await host.PostAsync("/api/simulation/stop");
        var afterRunStop = await host.MissionAsync();
        Assert.Equal("Scheduled", afterRunStop.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, afterRunStop.GetProperty("actualLaunchTimeUtc").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, afterRunStop.GetProperty("simulationLaunchTimeUtc").ValueKind);
        Assert.NotEmpty(await host.SamplesAsync("Starship"));
        AttackHost.AssertSaneKml(await host.KmlAsync("history"), "history after a stopped run");

        using var put = await host.PutT0Async("{\"launchTimeUtc\":\"2030-01-01T00:00:00Z\"}");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var afterPut = await host.MissionAsync();
        Assert.Equal(JsonValueKind.Null, afterPut.GetProperty("simulationLaunchTimeUtc").ValueKind);
        Assert.Equal("2030-01-01T00:00:00Z", afterPut.GetProperty("launchTimeUtc").GetString());
        Assert.Empty(await host.SamplesAsync("Starship"));
        Assert.Empty(await host.SamplesAsync("SuperHeavy"));
    }

    [Fact]
    public async Task MissionCodeCase_DoesNotBypassTheT0Lock()
    {
        await using var host = AttackHost.Start();
        var (status, _) = await host.TextAsync(HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"DEMO-IFT\",\"countdownSeconds\":60}");
        if (status != HttpStatusCode.OK)
        {
            Assert.Equal(HttpStatusCode.NotFound, status);
            return;
        }

        var (lowerPut, text) = await host.TextAsync(HttpMethod.Put, "/api/missions/demo-ift/launch-time", "{\"launchTimeUtc\":\"2031-01-01T00:00:00Z\"}");
        var (upperPut, _) = await host.TextAsync(HttpMethod.Put, "/api/missions/DEMO-IFT/launch-time", "{\"launchTimeUtc\":\"2031-01-01T00:00:00Z\"}");

        Assert.True(lowerPut == HttpStatusCode.Conflict, $"lower-case PUT during an active run gave {(int)lowerPut}: {text}");
        Assert.True(upperPut == HttpStatusCode.Conflict, $"upper-case PUT during an active run gave {(int)upperPut}");
    }

    [Fact]
    public async Task ParallelT0Puts_AndStart_NeverLeaveARunWithoutItsAnchor()
    {
        await using var host = AttackHost.Start();

        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 10; i++)
        {
            tasks.Add(host.PutT0Async($"{{\"launchTimeUtc\":\"{2030 + i}-01-01T00:00:00Z\"}}"));
            tasks.Add(host.SendAsync(HttpMethod.Post, "/api/simulation/start", "{\"mission\":\"demo-ift\",\"countdownSeconds\":600}"));
        }

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"{(int)r.StatusCode}"));
        var status = await host.StatusAsync();
        var mission = await host.MissionAsync();
        if (AttackHost.State(status) == "Countdown")
        {
            Assert.Equal(
                status.GetProperty("launchTimeUtc").GetString(),
                mission.GetProperty("simulationLaunchTimeUtc").GetString());
        }
    }

    [Fact]
    public async Task StaleRule_NotAppliedAfterStopOrCompletion_EvenWithAnOldSample()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 100, countdown: 0);
        await host.AdvanceAsync(86_400);
        await host.AdvanceAsync(3_600);
        var completed = await host.KmlTextAsync("live");
        Assert.DoesNotContain("STALE", completed, StringComparison.Ordinal);

        await host.StartAsync(speed: 10, countdown: 0);
        await host.AdvanceAsync(10);
        await host.PostAsync("/api/simulation/stop");
        await host.AdvanceAsync(3_600);

        Assert.DoesNotContain("STALE", await host.KmlTextAsync("live"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MinSpeed_OneTenth_EmitsNothingNewUntilASampleIsDue()
    {
        await using var host = AttackHost.Start();
        await host.StartAsync(speed: 0.1, countdown: 0);
        await host.AdvanceAsync(1);
        var first = (await host.SamplesAsync("Starship")).Count;

        await host.AdvanceAsync(1);
        await host.AdvanceAsync(1);
        var stillSame = (await host.SamplesAsync("Starship")).Count;
        await host.AdvanceAsync(20);

        Assert.Equal(first, stillSame);
        var samples = await host.SamplesAsync("Starship");
        Assert.True(samples.Count > stillSame);
        AssertOrderedAndUnique(samples, "min speed");
        Assert.Equal(2.3, AttackHost.Offset(await host.StatusAsync()), 1e-6);
    }
}
