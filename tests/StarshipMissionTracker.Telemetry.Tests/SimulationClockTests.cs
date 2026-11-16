using StarshipMissionTracker.Telemetry.Simulation;
using StarshipMissionTracker.Telemetry.Tests.Support;

namespace StarshipMissionTracker.Telemetry.Tests;

public class SimulationClockTests
{
    private static readonly DateTimeOffset T0 = TestData.Start;

    private static DateTimeOffset At(double wallSecondsAfterLaunch) => T0.AddSeconds(wallSecondsAfterLaunch);

    [Fact]
    public void BeforeLiftoff_OffsetIsNegativeWallSeconds()
    {
        var clock = new SimulationClock(T0, 10);
        Assert.Equal(-4, clock.OffsetAt(At(-4)), 9);
        Assert.Equal(0, clock.OffsetAt(At(0)), 9);
    }

    [Fact]
    public void AfterLiftoff_OffsetScalesWithSpeed()
    {
        var clock = new SimulationClock(T0, 10);
        Assert.Equal(50, clock.OffsetAt(At(5)), 9);
    }

    [Fact]
    public void SpeedChange_DoesNotJump_AndAppliesFromTheInstant()
    {
        var clock = new SimulationClock(T0, 1);
        var before = clock.OffsetAt(At(10));
        clock.SetSpeed(At(10), 10);
        Assert.Equal(before, clock.OffsetAt(At(10)), 9);
        Assert.Equal(10, clock.OffsetAt(At(10)), 9);
        Assert.Equal(20, clock.OffsetAt(At(11)), 9);

        clock.SetSpeed(At(11), 0.1);
        Assert.Equal(20, clock.OffsetAt(At(11)), 9);
        Assert.Equal(21, clock.OffsetAt(At(21)), 9);
    }

    [Fact]
    public void RepeatedSpeedChanges_AreContinuous()
    {
        var clock = new SimulationClock(T0, 2);
        double wall = 0;
        double expected = 0;
        foreach (var speed in new[] { 5.0, 0.5, 100, 0.1, 7.5 })
        {
            wall += 3;
            expected += 3 * clock.Speed;
            Assert.Equal(expected, clock.OffsetAt(At(wall)), 9);
            clock.SetSpeed(At(wall), speed);
            Assert.Equal(expected, clock.OffsetAt(At(wall)), 9);
        }
    }

    [Fact]
    public void Pause_FreezesOffset_AndResumeContinuesWithoutJump()
    {
        var clock = new SimulationClock(T0, 10);
        clock.Pause(At(2));
        Assert.Equal(20, clock.OffsetAt(At(2)), 9);
        Assert.Equal(20, clock.OffsetAt(At(500)), 9);

        clock.Resume(At(500));
        Assert.Equal(20, clock.OffsetAt(At(500)), 9);
        Assert.Equal(30, clock.OffsetAt(At(501)), 9);
    }

    [Fact]
    public void SpeedChangeWhilePaused_TakesEffectOnResume()
    {
        var clock = new SimulationClock(T0, 10);
        clock.Pause(At(2));
        clock.SetSpeed(At(50), 1);
        Assert.Equal(20, clock.OffsetAt(At(60)), 9);
        clock.Resume(At(60));
        Assert.Equal(25, clock.OffsetAt(At(65)), 9);
    }

    [Fact]
    public void SpeedChangeInCountdown_IsOnlyStored_LiftoffInstantUnchanged()
    {
        var clock = new SimulationClock(T0, 1);
        clock.SetSpeed(At(-6), 50);
        Assert.Equal(T0, clock.WallLaunchUtc);
        Assert.Equal(-6, clock.OffsetAt(At(-6)), 9);
        Assert.Equal(-1, clock.OffsetAt(At(-1)), 9);
        Assert.Equal(100, clock.OffsetAt(At(2)), 9);
    }

    [Fact]
    public void PauseInCountdown_Throws()
    {
        var clock = new SimulationClock(T0, 1);
        Assert.Throws<InvalidOperationException>(() => clock.Pause(At(-1)));
    }

    [Fact]
    public void DoublePause_AndResumeWithoutPause_Throw()
    {
        var clock = new SimulationClock(T0, 1);
        Assert.Throws<InvalidOperationException>(() => clock.Resume(At(1)));
        clock.Pause(At(1));
        Assert.Throws<InvalidOperationException>(() => clock.Pause(At(2)));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(1)]
    [InlineData(100)]
    public void BoundsAreAccepted(double speed)
    {
        var clock = new SimulationClock(T0, speed);
        clock.SetSpeed(At(1), speed);
        Assert.Equal(speed, clock.Speed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0999)]
    [InlineData(100.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidSpeed_IsRejected_NotClamped(double speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock(T0, speed));
        var clock = new SimulationClock(T0, 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSpeed(At(1), speed));
        Assert.Equal(5, clock.Speed);
    }
}
