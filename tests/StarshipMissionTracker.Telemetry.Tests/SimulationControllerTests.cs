
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Simulation;
using StarshipMissionTracker.Telemetry.Tests.Support;

namespace StarshipMissionTracker.Telemetry.Tests;

public class SimulationControllerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<string> Keys(IEnumerable<TelemetrySample> samples) =>
        samples.Select(s => $"{s.Vehicle}@{s.TimestampUtc:O}").OrderBy(k => k, StringComparer.Ordinal).ToList();

    [Fact]
    public void Initially_Idle_WithDefaultSpeed()
    {
        var rig = new TestData.Rig();
        var status = rig.Controller.GetStatus();
        Assert.Equal(SimulationState.Idle, status.State);
        Assert.Equal(SimulationSpeedLimits.Default, status.Speed);
        Assert.Null(status.MissionCode);
        Assert.Null(status.CurrentOffsetSeconds);
    }

    [Fact]
    public async Task Countdown_Running_Completed()
    {
        var rig = new TestData.Rig();
        var started = await rig.Start(speed: 1, countdown: 10);
        Assert.Equal(SimulationState.Countdown, started.State);
        Assert.Equal(-10, started.CurrentOffsetSeconds);
        Assert.Equal(TestData.Start.AddSeconds(10), started.LaunchTimeUtc);
        Assert.Equal(1, rig.Telemetry.DeleteCalls);
        Assert.Equal((MissionStatus.Scheduled, (DateTimeOffset?)null, (DateTimeOffset?)TestData.Start.AddSeconds(10)), rig.Missions.StateCalls[0]);

        await rig.Run(9.75);
        Assert.Equal(SimulationState.Countdown, rig.Controller.GetStatus().State);
        Assert.Empty(rig.Telemetry.Samples);

        await rig.Run(0.25);
        var running = rig.Controller.GetStatus();
        Assert.Equal(SimulationState.Running, running.State);
        Assert.Equal(0, running.CurrentOffsetSeconds);
        Assert.Equal(2, rig.Telemetry.Samples.Count); // offset 0 of both vehicles
        Assert.Equal(MissionStatus.InFlight, rig.Missions.StateCalls[1].Status);
        Assert.Equal(TestData.Start.AddSeconds(10), rig.Missions.StateCalls[1].Actual);

        await rig.Run(25);
        var done = rig.Controller.GetStatus();
        Assert.Equal(SimulationState.Completed, done.State);
        Assert.Equal(MissionStatus.Completed, rig.Missions.StateCalls[^1].Status);
        Assert.Equal(30, rig.Telemetry.Samples.Count); // 21 Starship + 9 Super Heavy
        Assert.All(rig.Telemetry.Samples, s => Assert.Equal(DataQuality.Simulated, s.Quality));

        var frozen = done.CurrentOffsetSeconds;
        rig.Time.Advance(TimeSpan.FromSeconds(100));
        Assert.Equal(frozen, rig.Controller.GetStatus().CurrentOffsetSeconds);
    }

    [Fact]
    public async Task SampleSet_AfterVaryingSpeeds_EqualsConstantSpeed()
    {
        var constant = new TestData.Rig();
        await constant.Start(speed: 1, countdown: 10);
        await constant.Run(40);
        Assert.Equal(SimulationState.Completed, constant.Controller.GetStatus().State);

        var varying = new TestData.Rig();
        await varying.Start(speed: 1, countdown: 10);
        await varying.Controller.SetSpeedAsync(0.5, Ct); // in countdown: stored
        await varying.Run(10);
        await varying.Run(4); // t = 2
        await varying.Controller.SetSpeedAsync(5, Ct);
        await varying.Run(1); // t = 7
        await varying.Controller.PauseAsync(Ct);
        await varying.Run(3);
        await varying.Controller.ResumeAsync(Ct);
        await varying.Controller.SetSpeedAsync(0.1, Ct);
        await varying.Run(10); // t = 8
        await varying.Controller.SetSpeedAsync(100, Ct);
        await varying.Run(1);
        Assert.Equal(SimulationState.Completed, varying.Controller.GetStatus().State);

        Assert.Equal(Keys(constant.Telemetry.Samples), Keys(varying.Telemetry.Samples));
        foreach (var group in varying.Telemetry.Samples.GroupBy(s => s.Vehicle))
        {
            var times = group.Select(s => s.TimestampUtc).ToList();
            Assert.Equal(times.OrderBy(t => t).ToList(), times);
        }
    }

    [Fact]
    public async Task SpeedChange_InCountdown_AppliesFromLiftoff_LiftoffUnchanged()
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 10);
        await rig.Run(4);
        var changed = await rig.Controller.SetSpeedAsync(10, Ct);
        Assert.Equal(SimulationState.Countdown, changed.State);
        Assert.Equal(10, changed.Speed);
        Assert.Equal(-6, changed.CurrentOffsetSeconds);
        Assert.Equal(TestData.Start.AddSeconds(10), changed.LaunchTimeUtc);

        await rig.Run(7); // 6 s countdown + 1 s flight at 10x
        Assert.Equal(10, rig.Controller.GetStatus().CurrentOffsetSeconds);
    }

    [Fact]
    public async Task SpeedChange_WhileRunning_HasNoJump()
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 0);
        await rig.Run(5);
        var before = rig.Controller.GetStatus().CurrentOffsetSeconds;
        var after = await rig.Controller.SetSpeedAsync(10, Ct);
        Assert.Equal(before, after.CurrentOffsetSeconds);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(before + 10, rig.Controller.GetStatus().CurrentOffsetSeconds);
    }

    [Fact]
    public async Task Pause_FreezesTime_NoSamples_ResumeContinues()
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 0);
        await rig.Run(3);
        var count = rig.Telemetry.Samples.Count;
        var paused = await rig.Controller.PauseAsync(Ct);
        Assert.True(paused.IsPaused);
        Assert.Equal(3, paused.CurrentOffsetSeconds);

        await rig.Run(60);
        Assert.Equal(count, rig.Telemetry.Samples.Count);
        Assert.Equal(3, rig.Controller.GetStatus().CurrentOffsetSeconds);
        Assert.Equal(SimulationState.Running, rig.Controller.GetStatus().State);

        var resumed = await rig.Controller.ResumeAsync(Ct);
        Assert.False(resumed.IsPaused);
        Assert.Equal(3, resumed.CurrentOffsetSeconds);
        await rig.Run(1);
        Assert.Equal(4, rig.Controller.GetStatus().CurrentOffsetSeconds);
    }

    [Fact]
    public async Task Pause_InCountdown_IsRejected()
    {
        var rig = new TestData.Rig();
        await rig.Start(countdown: 10);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.PauseAsync(Ct));
        Assert.False(rig.Controller.GetStatus().IsPaused);
    }

    [Fact]
    public async Task Pause_RightAfterLiftoff_BeforeTick_IsAccepted()
    {
        var rig = new TestData.Rig();
        await rig.Start(countdown: 2);
        rig.Time.Advance(TimeSpan.FromSeconds(2.1));
        var status = await rig.Controller.PauseAsync(Ct);
        Assert.Equal(SimulationState.Running, status.State);
        Assert.True(status.IsPaused);
    }

    [Fact]
    public async Task WrongStateCalls_AreRejected()
    {
        var rig = new TestData.Rig();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.SetSpeedAsync(5, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.PauseAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.ResumeAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.StopAsync(Ct));

        await rig.Start(countdown: 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.ResumeAsync(Ct)); // not paused
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Start()); // already running
        await rig.Controller.PauseAsync(Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.PauseAsync(Ct)); // already paused
        await rig.Controller.ResumeAsync(Ct);

        await rig.Run(30);
        Assert.Equal(SimulationState.Completed, rig.Controller.GetStatus().State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.SetSpeedAsync(5, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.PauseAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.ResumeAsync(Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0999)]
    [InlineData(100.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task InvalidSpeed_IsRejected_AndSpeedIsKept(double speed)
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 2, countdown: 0);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rig.Controller.SetSpeedAsync(speed, Ct));
        Assert.Equal(2, rig.Controller.GetStatus().Speed);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(100)]
    public async Task BoundarySpeeds_AreAccepted(double speed)
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 2, countdown: 0);
        Assert.Equal(speed, (await rig.Controller.SetSpeedAsync(speed, Ct)).Speed);
    }

    [Fact]
    public async Task InvalidSpeed_InIdle_ReportsBadArgumentFirst()
    {
        var rig = new TestData.Rig();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rig.Controller.SetSpeedAsync(double.NaN, Ct));
    }

    [Fact]
    public async Task StopAndRestart_ReplaysFromTheStart_WithoutDuplicates()
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 0);
        await rig.Run(5);
        Assert.NotEmpty(rig.Telemetry.Samples);

        var stopped = await rig.Controller.StopAsync(Ct);
        Assert.Equal(SimulationState.Idle, stopped.State);
        await rig.Run(5);
        Assert.Equal(MissionStatus.Scheduled, rig.Missions.StateCalls[^1].Status);

        var restartedAt = rig.Time.GetUtcNow();
        await rig.Start(speed: 1, countdown: 0);
        Assert.Equal(2, rig.Telemetry.DeleteCalls);
        Assert.Empty(rig.Telemetry.Samples);
        await rig.Run(2);

        var keys = Keys(rig.Telemetry.Samples);
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Contains(rig.Telemetry.Samples, s => s.TimestampUtc == restartedAt);
        Assert.Equal(restartedAt.AddSeconds(2), rig.Telemetry.Samples.Where(s => s.Vehicle == VehicleType.Starship).Max(s => s.TimestampUtc));
    }

    [Fact]
    public async Task Restart_AfterCompleted_IsAllowed()
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 0);
        await rig.Run(30);
        Assert.Equal(SimulationState.Completed, rig.Controller.GetStatus().State);
        var again = await rig.Start(speed: 5, countdown: 3);
        Assert.Equal(SimulationState.Countdown, again.State);
        Assert.Equal(5, again.Speed);
    }

    [Fact]
    public async Task Start_UnknownMission_AndNonSimulated_AreRejected()
    {
        var rig = new TestData.Rig();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            rig.Controller.StartAsync(new SimulationStartRequest("nope", 1, 0), Ct));
        Assert.Equal(SimulationState.Idle, rig.Controller.GetStatus().State);

        var real = new TestData.Rig(simulated: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => real.Start());
        Assert.Equal(0, real.Telemetry.DeleteCalls);
    }

    [Fact]
    public async Task FailedWrite_IsRetried_WithoutLosingOrDuplicatingSamples()
    {
        var reference = new TestData.Rig();
        await reference.Start(speed: 1, countdown: 0);
        await reference.Run(30);

        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 0);
        await rig.Run(3);
        rig.Telemetry.FailNextAdds = 1;
        await rig.Run(2);
        await rig.Run(30);

        Assert.Equal(SimulationState.Completed, rig.Controller.GetStatus().State);
        Assert.Equal(Keys(reference.Telemetry.Samples), Keys(rig.Telemetry.Samples));
    }

    [Fact]
    public async Task ConsecutiveFailures_StopTheRun_WithReason()
    {
        var rig = new TestData.Rig(maxFailures: 3);
        await rig.Start(speed: 1, countdown: 0);
        rig.Telemetry.FailNextAdds = 10;
        await rig.Run(2);
        var status = rig.Controller.GetStatus();
        Assert.Equal(SimulationState.Idle, status.State);
        Assert.Equal("Stopped after repeated storage errors", status.Reason);
        Assert.DoesNotContain("simulated database failure", status.Reason);
        Assert.Contains(rig.Logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ConsecutiveFailures_AlsoResetStatusAndPlannedTime()
    {
        var rig = new TestData.Rig(maxFailures: 3);
        await rig.Start(speed: 1, countdown: 10);
        Assert.Equal(TestData.Start.AddSeconds(10), rig.Missions.StoredPlanned); // simulated T0 differs from the original
        await rig.Run(11);
        Assert.Equal(MissionStatus.InFlight, rig.Missions.StoredStatus);

        rig.Telemetry.FailNextAdds = 10;
        await rig.Run(5); // samples are due once per mission second, so each failing second counts once
        Assert.Equal(SimulationState.Idle, rig.Controller.GetStatus().State);
        Assert.Equal(MissionStatus.Scheduled, rig.Missions.StoredStatus);
        Assert.Equal(TestData.Start, rig.Missions.StoredPlanned);
    }

    [Fact]
    public async Task Stop_WithCancelledToken_StillRestoresStatusAndPlannedTime()
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 10);
        Assert.Equal(TestData.Start.AddSeconds(10), rig.Missions.StoredPlanned);
        using var cts = new CancellationTokenSource();
        rig.Missions.BeforeStateWrite = () => cts.Cancel(); // the caller cancels while the restore is running

        var status = await rig.Controller.StopAsync(cts.Token);

        Assert.Equal(SimulationState.Idle, status.State);
        Assert.Equal(MissionStatus.Scheduled, rig.Missions.StoredStatus);
        Assert.Equal(TestData.Start, rig.Missions.StoredPlanned);
    }

    [Fact]
    public async Task Start_UnknownMission_IsLoggedAndThrows()
    {
        var rig = new TestData.Rig();
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => rig.Controller.StartAsync(new SimulationStartRequest("nope", 1, 0), Ct));
        Assert.Contains(rig.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("unknown mission"));
    }

    [Fact]
    public async Task ConsecutiveFailures_ResetFailing_IsLoggedNotThrown()
    {
        var rig = new TestData.Rig(maxFailures: 1);
        await rig.Start(speed: 1, countdown: 0);
        rig.Telemetry.FailNextAdds = 10;
        rig.Missions.FailAlwaysFor.Add(MissionStatus.Scheduled);
        await rig.Run(0.25);
        Assert.Equal(SimulationState.Idle, rig.Controller.GetStatus().State);
        Assert.Contains(rig.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Could not reset"));
    }

    [Fact]
    public async Task CompletedWriteFailure_DoesNotReinsertSamples_AndIsRetried()
    {
        var reference = new TestData.Rig();
        await reference.Start(speed: 1, countdown: 0);
        await reference.Run(30);

        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 0);
        rig.Missions.FailOnceFor.Add(MissionStatus.Completed);
        await rig.Run(30);

        Assert.Equal(SimulationState.Completed, rig.Controller.GetStatus().State);
        Assert.Equal(MissionStatus.Completed, rig.Missions.StoredStatus);
        Assert.Equal(2, rig.Missions.StateCalls.Count(c => c.Status == MissionStatus.Completed));
        var keys = Keys(rig.Telemetry.Samples);
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(Keys(reference.Telemetry.Samples), keys);
    }

    [Fact]
    public async Task InFlightWriteFailure_IsRetried_WithoutLosingSamples()
    {
        var reference = new TestData.Rig();
        await reference.Start(speed: 1, countdown: 0);
        await reference.Run(30);

        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 0);
        rig.Missions.FailOnceFor.Add(MissionStatus.InFlight);
        await rig.Run(30);

        Assert.Equal(SimulationState.Completed, rig.Controller.GetStatus().State);
        Assert.Equal(2, rig.Missions.StateCalls.Count(c => c.Status == MissionStatus.InFlight));
        var keys = Keys(rig.Telemetry.Samples);
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(Keys(reference.Telemetry.Samples), keys);
    }

    [Fact]
    public async Task Stop_DuringCountdownOrRunning_RestoresOriginalPlannedTime()
    {
        var rig = new TestData.Rig();
        var original = TestData.Start;
        await rig.Start(speed: 1, countdown: 10);
        Assert.Equal(TestData.Start.AddSeconds(10), rig.Missions.StoredPlanned);
        await rig.Controller.StopAsync(Ct);
        Assert.Equal(MissionStatus.Scheduled, rig.Missions.StoredStatus);
        Assert.Equal(original, rig.Missions.StoredPlanned);

        await rig.Start(speed: 1, countdown: 0);
        await rig.Run(3);
        await rig.Controller.StopAsync(Ct);
        Assert.Equal(MissionStatus.Scheduled, rig.Missions.StoredStatus);
        Assert.NotNull(rig.Missions.StoredActual); // contract limit: Actual cannot be cleared through SetFlightStateAsync
    }

    [Fact]
    public async Task Stop_AfterCompleted_KeepsStoredResult()
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 0);
        await rig.Run(30);
        var planned = rig.Missions.StoredPlanned;
        await rig.Controller.StopAsync(Ct);
        Assert.Equal(MissionStatus.Completed, rig.Missions.StoredStatus);
        Assert.Equal(planned, rig.Missions.StoredPlanned);
    }

    [Fact]
    public async Task StatusWrite_ReturningFalse_IsLogged()
    {
        var rig = new TestData.Rig();
        rig.Missions.ReturnFalse = true;
        await rig.Start(speed: 1, countdown: 0);
        Assert.Contains(rig.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("was not stored"));
    }

    [Fact]
    public async Task Start_DeleteFailure_RestoresPlannedTime_AndStaysIdle()
    {
        var rig = new TestData.Rig();
        rig.Telemetry.FailDelete = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Start(speed: 1, countdown: 10));
        Assert.Equal(SimulationState.Idle, rig.Controller.GetStatus().State);
        Assert.Equal(TestData.Start, rig.Missions.StoredPlanned);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(1000)]
    public void InvalidOptions_ThrowArgumentException(double speed)
    {
        var options = new SimulationOptions { DefaultSpeed = speed };
        Assert.Throws<ArgumentException>(() => new SimulationController(
            new FakeScopeFactory(new FakeMissionRepository { Mission = TestData.Mission() }, new FakeTelemetryRepository()),
            TimeProvider.System, options, NullLogger<SimulationController>.Instance));
        Assert.Throws<ArgumentException>(() => new SimulationController(
            new FakeScopeFactory(new FakeMissionRepository { Mission = TestData.Mission() }, new FakeTelemetryRepository()),
            TimeProvider.System, new SimulationOptions { MaxConsecutiveFailures = 0 }, NullLogger<SimulationController>.Instance));
    }

    [Fact]
    public async Task Concurrent_Calls_KeepStateConsistent()
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 0);
        var tasks = Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
        {
            await rig.Controller.SetSpeedAsync(0.5 + (i % 10));
            _ = rig.Controller.GetStatus();
            await rig.Controller.TickAsync(Ct);
        }));
        await Task.WhenAll(tasks);
        Assert.Equal(SimulationState.Running, rig.Controller.GetStatus().State);
    }
}
