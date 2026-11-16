using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Data;

namespace StarshipMissionTracker.Api.Startup;

/// <summary>
/// Migrates the database and imports the mission files from <c>data/missions</c> when the host starts. Registered before
/// the simulation hosted service: hosted services start one after another in registration order, so the database is
/// ready (and an auto-start mission exists) before the simulator runs, and Kestrel accepts requests only after both.
/// A failing migration stops the host; a single broken mission file is logged and skipped (the others still load).
/// </summary>
public sealed class DatabaseStartupService(
    DatabaseInitializer initializer,
    IMissionImporter importer,
    StarshipPaths paths,
    ILogger<DatabaseStartupService> logger) : IHostedService
{
    /// <summary>Number of finished runs (test hook for the start order; internal, visible to the Api tests).</summary>
    internal int CompletedRuns { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Database {DatabaseFile}: initialising", paths.DatabaseFile);
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var files = Directory.GetFiles(paths.MissionsDirectory, "*.json").Order(StringComparer.Ordinal).ToList();
        int imported = 0, present = 0, failed = 0;
        foreach (var file in files)
        {
            try
            {
                if (await importer.ImportIfMissingAsync(file, paths.TrajectoriesDirectory, cancellationToken).ConfigureAwait(false))
                {
                    imported++;
                }
                else
                {
                    present++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any failure of one file (invalid data, I/O, database) is logged with the file name; the others still load.
                failed++;
                logger.LogError(ex, "Mission import failed for {MissionFile}: {Message}", Path.GetFileName(file), ex.Message);
            }
        }

        logger.LogInformation(
            "Mission import from {MissionsDirectory}: {FileCount} file(s), {Imported} imported, {Present} already present, {Failed} failed",
            paths.MissionsDirectory, files.Count, imported, present, failed);
        CompletedRuns++;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
