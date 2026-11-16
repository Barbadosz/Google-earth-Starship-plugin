using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Telemetry.Tests.Support;

namespace StarshipMissionTracker.Telemetry.Tests;

/// <summary><see cref="Simulation.SimulationController.RunWhileInactiveAsync{T}"/>: state guard and serialisation with Start.</summary>
public class SimulationControllerRunWhileInactiveTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Idle_RunsTheAction_WithTheCallerToken()
    {
        var rig = new TestData.Rig();
        CancellationToken seen = default;
        var result = await rig.Controller.RunWhileInactiveAsync("demo", ct =>
        {
            seen = ct;
            return Task.FromResult(42);
        }, Ct);

        Assert.Equal(42, result);
        Assert.Equal(Ct, seen);
    }

    [Theory]
    [InlineData(false, false)] // countdown
    [InlineData(true, false)] // running
    [InlineData(true, true)] // running, paused
    public async Task ActiveRunOfTheMission_IsRejected_AndTheActionIsNotCalled(bool liftOff, bool pause)
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 10);
        if (liftOff)
        {
            await rig.Run(11);
        }

        if (pause)
        {
            await rig.Controller.PauseAsync(Ct);
        }

        var called = false;
        var ex = await Assert.ThrowsAsync<SimulationStateException>(() =>
            rig.Controller.RunWhileInactiveAsync("DEMO", _ =>
            {
                called = true;
                return Task.FromResult(true);
            }, Ct));

        Assert.False(called);
        Assert.Contains(pause ? "paused" : liftOff ? "Running" : "Countdown", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletedRun_AndRunOfAnotherMission_DoNotBlock()
    {
        var rig = new TestData.Rig();
        await rig.Start(speed: 1, countdown: 10);
        Assert.True(await rig.Controller.RunWhileInactiveAsync("other", _ => Task.FromResult(true), Ct));

        await rig.Run(40);
        Assert.Equal(SimulationState.Completed, rig.Controller.GetStatus().State);
        Assert.True(await rig.Controller.RunWhileInactiveAsync("demo", _ => Task.FromResult(true), Ct));
    }

    [Fact]
    public async Task ConcurrentStart_WaitsUntilTheActionHasFinished()
    {
        var rig = new TestData.Rig();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateDuringAction = (SimulationState?)null;

        var guarded = rig.Controller.RunWhileInactiveAsync("demo", async ct =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            stateDuringAction = rig.Controller.GetStatus().State;
            return true;
        }, Ct);
        await entered.Task.WaitAsync(Ct);

        var start = rig.Start(speed: 1, countdown: 10);
        await Task.Yield();
        Assert.False(start.IsCompleted); // blocked on the operation lock
        Assert.Equal(SimulationState.Idle, rig.Controller.GetStatus().State);

        release.SetResult();
        Assert.True(await guarded);
        var started = await start;

        Assert.Equal(SimulationState.Idle, stateDuringAction);
        Assert.Equal(SimulationState.Countdown, started.State);
    }

    [Fact]
    public async Task ActionFailure_ReleasesTheLock()
    {
        var rig = new TestData.Rig();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            rig.Controller.RunWhileInactiveAsync<bool>("demo", _ => throw new TimeoutException("db"), Ct));

        var started = await rig.Start(speed: 1, countdown: 10);
        Assert.Equal(SimulationState.Countdown, started.State);
    }
}
