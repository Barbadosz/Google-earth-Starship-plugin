using Microsoft.Extensions.Logging;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;

namespace StarshipMissionTracker.Data.Import;

/// <summary>
/// Reads a mission file with its planned trajectories through <see cref="IMissionLoader"/> (the M1 file loader) and stores it
/// when the mission code is not in the database yet. After the first import the database is the T0 truth: the file's
/// launch time is never applied again.
/// </summary>
public sealed class MissionImporter(
    IMissionLoader loader,
    IMissionRepository repository,
    ILogger<MissionImporter> logger) : IMissionImporter
{
    public async Task<bool> ImportIfMissingAsync(string missionFilePath, string trajectoriesDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(trajectoriesDirectory);

        var mission = await loader.LoadAsync(missionFilePath, trajectoriesDirectory, null, cancellationToken);

        // Simulated only when every stored path is Simulated; a mixed or better mission is never treated as a simulator target.
        var paths = mission.Vehicles.SelectMany(v => v.FlightPaths).ToList();
        var isSimulated = paths.Count > 0 && paths.All(p => p.Quality == DataQuality.Simulated);

        var imported = await repository.ImportAsync(mission, isSimulated, cancellationToken);
        logger.LogInformation(
            "Mission file {MissionFile} (mission {MissionCode}): {Result}",
            Path.GetFileName(missionFilePath), mission.MissionCode, imported ? "imported" : "already present");
        return imported;
    }
}
