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

    /// <summary>The mission with its clock built from <c>PlannedLaunchTimeUtc</c>, planned paths and events; null when unknown.</summary>
    Task<Mission?> GetPlannedMissionAsync(string missionCode, CancellationToken cancellationToken = default);

    /// <summary>Stores the mission if its code is absent. Returns true when imported, false when it already existed.</summary>
    Task<bool> ImportAsync(Mission mission, bool isSimulated, CancellationToken cancellationToken = default);

    /// <summary>Sets T0. Returns false when the mission is unknown.</summary>
    Task<bool> SetPlannedLaunchTimeAsync(string missionCode, DateTimeOffset plannedLaunchTimeUtc, CancellationToken cancellationToken = default);

    /// <summary>Updates status and, when given, actual and/or planned launch time. Returns false when the mission is unknown.</summary>
    Task<bool> SetFlightStateAsync(
        string missionCode,
        MissionStatus status,
        DateTimeOffset? actualLaunchTimeUtc,
        DateTimeOffset? plannedLaunchTimeUtc,
        CancellationToken cancellationToken = default);
}
