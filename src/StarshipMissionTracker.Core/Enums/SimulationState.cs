namespace StarshipMissionTracker.Core.Enums;

/// <summary>State of the telemetry simulation (in memory only, not persisted).</summary>
public enum SimulationState
{
    Idle,
    Countdown,
    Running,
    Completed
}
