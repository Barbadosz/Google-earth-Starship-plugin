using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Interfaces;

/// <summary>Persistence of missions (header, planned paths, events). The database is the T0 truth after the first import.</summary>
public interface IMissionRepository
{
    Task<IReadOnlyList<MissionInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Null when the code is unknown.</summary>
    Task<MissionInfo?> GetInfoAsync(string missionCode, CancellationToken cancellationToken = default);

    /// <summary>The in-flight mission, else the newest; null when there is none.</summary>
    Task<MissionInfo?> GetCurrentInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The mission with its clock and config T0 built from the effective T0 (<see cref="MissionInfo.LaunchTimeUtc"/>:
    /// simulation anchor, else planned), planned paths and events; null when unknown.
    /// </summary>
    Task<Mission?> GetPlannedMissionAsync(string missionCode, CancellationToken cancellationToken = default);

    /// <summary>Stores the mission if its code is absent. Returns true when imported, false when it already existed.</summary>
    Task<bool> ImportAsync(Mission mission, bool isSimulated, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the planned T0 and, in one transaction, clears the simulation anchor and the actual launch time, sets status
    /// Scheduled and deletes the mission's Simulated telemetry samples (never samples of another quality), so the new T0
    /// is the effective T0 at once. The caller must make sure no simulation of the mission is active.
    /// Returns false when the mission is unknown.
    /// </summary>
    Task<bool> SetPlannedLaunchTimeAsync(string missionCode, DateTimeOffset plannedLaunchTimeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a simulation run in one transaction: status Scheduled, simulation anchor set, actual launch time cleared and
    /// the mission's Simulated telemetry samples deleted (other qualities and other missions untouched). The planned T0
    /// is never written. On any failure nothing is changed. Returns false when the mission is unknown.
    /// </summary>
    /// <exception cref="ArgumentException">The time is not UTC.</exception>
    Task<bool> BeginSimulationRunAsync(string missionCode, DateTimeOffset simulationLaunchTimeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates status, the actual launch time and the simulation anchor; never the planned T0. A null time leaves the
    /// stored value unchanged unless its clear flag is true. Returns false when the mission is unknown.
    /// </summary>
    /// <exception cref="ArgumentException">A time is not UTC, or a time is given together with its clear flag.</exception>
    Task<bool> SetFlightStateAsync(
        string missionCode,
        MissionStatus status,
        DateTimeOffset? actualLaunchTimeUtc,
        DateTimeOffset? simulationLaunchTimeUtc,
        bool clearActualLaunchTime = false,
        bool clearSimulationLaunchTime = false,
        CancellationToken cancellationToken = default);
}
