using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Telemetry.Simulation;

/// <summary>
/// Virtual mission clock with re-anchoring (plan section 8). Pure: no I/O, the caller passes the wall-clock instant.
/// Before liftoff the offset is <c>-(wallLaunch - now)</c> in wall seconds. From liftoff
/// <c>t(now) = anchorOffset + (now - anchorWall) * speed</c> (frozen at <c>anchorOffset</c> while paused).
/// Speed changes, pause and resume re-anchor at the change instant, so the mission offset never jumps.
/// <see cref="WallLaunchUtc"/> (T0) never changes. Not thread safe: the controller guards it with its lock.
/// </summary>
public sealed class SimulationClock
{
    private DateTimeOffset _anchorWall;
    private double _anchorOffsetSeconds;

    public SimulationClock(DateTimeOffset wallLaunchUtc, double speed)
    {
        RequireSpeed(speed);
        WallLaunchUtc = wallLaunchUtc;
        Speed = speed;
        _anchorWall = wallLaunchUtc;
        _anchorOffsetSeconds = 0;
    }

    /// <summary>The wall instant of liftoff (T0).</summary>
    public DateTimeOffset WallLaunchUtc { get; }

    public double Speed { get; private set; }

    public bool IsPaused { get; private set; }

    public bool HasLiftedOff(DateTimeOffset now) => now >= WallLaunchUtc;

    /// <summary>Mission offset (T+ seconds, negative before liftoff) at <paramref name="now"/>.</summary>
    public double OffsetAt(DateTimeOffset now)
    {
        if (now < WallLaunchUtc)
        {
            return -(WallLaunchUtc - now).TotalSeconds;
        }

        return IsPaused ? _anchorOffsetSeconds : _anchorOffsetSeconds + ((now - _anchorWall).TotalSeconds * Speed);
    }

    /// <summary>Changes the speed. During the countdown the value is only stored (it applies from liftoff).</summary>
    public void SetSpeed(DateTimeOffset now, double speed)
    {
        RequireSpeed(speed);
        if (HasLiftedOff(now))
        {
            _anchorOffsetSeconds = OffsetAt(now);
            _anchorWall = now;
        }

        Speed = speed;
    }

    public void Pause(DateTimeOffset now)
    {
        if (!HasLiftedOff(now))
        {
            throw new InvalidOperationException("Pause is not allowed during the countdown.");
        }

        if (IsPaused)
        {
            throw new InvalidOperationException("The simulation is already paused.");
        }

        _anchorOffsetSeconds = OffsetAt(now);
        _anchorWall = now;
        IsPaused = true;
    }

    public void Resume(DateTimeOffset now)
    {
        if (!IsPaused)
        {
            throw new InvalidOperationException("The simulation is not paused.");
        }

        _anchorWall = now;
        IsPaused = false;
    }

    private static void RequireSpeed(double speed)
    {
        if (!SimulationSpeedLimits.IsValid(speed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed), speed, $"Speed must be finite and within [{SimulationSpeedLimits.Min}, {SimulationSpeedLimits.Max}].");
        }
    }
}
