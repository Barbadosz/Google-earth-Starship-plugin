namespace StarshipMissionTracker.Core.Models;

/// <summary>Allowed replay speed (multiple of real time) of the simulation.</summary>
public static class SimulationSpeedLimits
{
    public const double Min = 0.1;
    public const double Max = 100;
    public const double Default = 10;

    /// <summary>True for a finite value in [<see cref="Min"/>, <see cref="Max"/>] (bounds included).</summary>
    public static bool IsValid(double speed) => double.IsFinite(speed) && speed >= Min && speed <= Max;
}
