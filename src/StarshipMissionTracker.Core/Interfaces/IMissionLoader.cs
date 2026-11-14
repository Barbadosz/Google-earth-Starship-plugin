using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Interfaces;

public interface IMissionLoader
{
    /// <summary>
    /// Loads the mission config and the planned trajectories next to it. T0 = override if given, else the config's launchTimeUtc;
    /// if neither exists throws <see cref="MissionDataException"/> (no default T0).
    /// </summary>
    Task<Mission> LoadAsync(
        string missionConfigPath,
        string trajectoriesDirectory,
        DateTimeOffset? launchTimeOverrideUtc,
        CancellationToken cancellationToken = default);
}
