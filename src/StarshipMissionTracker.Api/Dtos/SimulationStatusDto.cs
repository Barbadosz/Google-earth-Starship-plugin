using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Dtos;

/// <summary>
/// Simulation status JSON (contract of the control page, plan sections 4 and 8a): <c>state</c> (Idle / Countdown /
/// Running / Completed; a paused run is Running with <c>paused</c> true), <c>missionOffsetSeconds</c> = T+ (negative in
/// the countdown), <c>launchTimeUtc</c> = T0 of the run, <c>speedMin</c>/<c>speedMax</c> = accepted speed range.
/// <c>dataQuality</c> is always "Simulated": these runs are a replay of the planned path, never measured telemetry.
/// </summary>
public sealed record SimulationStatusDto(
    string State,
    string? Mission,
    double Speed,
    bool Paused,
    double? MissionOffsetSeconds,
    string? LaunchTimeUtc,
    string? Reason,
    double SpeedMin,
    double SpeedMax,
    string DataQuality)
{
    public static SimulationStatusDto From(SimulationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new SimulationStatusDto(
            status.State.ToString(),
            status.MissionCode,
            status.Speed,
            status.IsPaused,
            status.CurrentOffsetSeconds,
            ApiTime.ToText(status.LaunchTimeUtc),
            status.Reason,
            SimulationSpeedLimits.Min,
            SimulationSpeedLimits.Max,
            nameof(Core.Enums.DataQuality.Simulated));
    }
}
