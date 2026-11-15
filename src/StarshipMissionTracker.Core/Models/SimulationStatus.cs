using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Core.Models;

/// <summary>Snapshot of the simulation: state, speed, pause flag, T0 and the current mission offset (T+ seconds).</summary>
public sealed record SimulationStatus(
    SimulationState State,
    string? MissionCode,
    double Speed,
    bool IsPaused,
    DateTimeOffset? LaunchTimeUtc,
    double? CurrentOffsetSeconds,
    string? Reason);
