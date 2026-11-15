namespace StarshipMissionTracker.Core.Interfaces;

public interface IMissionImporter
{
    /// <summary>Imports the mission file (and its trajectories) when its code is not in the database yet. Returns true when imported.</summary>
    Task<bool> ImportIfMissingAsync(string missionFilePath, string trajectoriesDirectory, CancellationToken cancellationToken = default);
}
