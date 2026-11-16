using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Data.Mapping;

namespace StarshipMissionTracker.Data.Repositories;

public sealed class MissionRepository(
    IDbContextFactory<StarshipDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<MissionRepository> logger) : IMissionRepository
{
    public async Task<IReadOnlyList<MissionInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Missions.AsNoTracking()
            .OrderBy(m => m.PlannedLaunchTimeUtc).ThenBy(m => m.MissionCode)
            .ToListAsync(cancellationToken);
        var sites = await LaunchSitesAsync(db, cancellationToken);
        return rows.Select(r => MissionMapper.ToInfo(r, sites.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<MissionInfo?> GetInfoAsync(string missionCode, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Missions.AsNoTracking().FirstOrDefaultAsync(m => m.MissionCode == missionCode, cancellationToken);
        return row is null ? null : MissionMapper.ToInfo(row, (await LaunchSitesAsync(db, cancellationToken, row.Id)).GetValueOrDefault(row.Id));
    }

    public async Task<MissionInfo?> GetCurrentInfoAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Missions.AsNoTracking();
        var row = await query.Where(m => m.Status == MissionStatus.InFlight)
                      .OrderByDescending(m => m.PlannedLaunchTimeUtc).ThenBy(m => m.MissionCode)
                      .FirstOrDefaultAsync(cancellationToken)
                  ?? await query.OrderByDescending(m => m.PlannedLaunchTimeUtc).ThenBy(m => m.MissionCode)
                      .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : MissionMapper.ToInfo(row, (await LaunchSitesAsync(db, cancellationToken, row.Id)).GetValueOrDefault(row.Id));
    }

    /// <summary>Launch site per mission = position of its first positioned Liftoff event (not stored as a column).</summary>
    private static async Task<Dictionary<Guid, (double Latitude, double Longitude)?>> LaunchSitesAsync(
        StarshipDbContext db, CancellationToken cancellationToken, Guid? missionId = null)
    {
        var rows = await db.MissionEvents.AsNoTracking()
            .Where(e => e.Type == EventType.Liftoff && e.Latitude != null && e.Longitude != null
                        && (missionId == null || e.MissionId == missionId))
            .OrderBy(e => e.OffsetSeconds)
            .Select(e => new { e.MissionId, Lat = e.Latitude!.Value, Lon = e.Longitude!.Value })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.MissionId)
            .ToDictionary(g => g.Key, g => ((double Latitude, double Longitude)?)(g.First().Lat, g.First().Lon));
    }

    public async Task<Mission?> GetPlannedMissionAsync(string missionCode, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Missions.AsNoTracking().AsSplitQuery()
            .Include(m => m.Events)
            .Include(m => m.Vehicles).ThenInclude(v => v.Trajectories).ThenInclude(t => t.Points)
            .FirstOrDefaultAsync(m => m.MissionCode == missionCode, cancellationToken);
        return row is null ? null : MissionMapper.ToMission(row);
    }

    public async Task<bool> ImportAsync(Mission mission, bool isSimulated, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mission);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (await db.Missions.AnyAsync(m => m.MissionCode == mission.MissionCode, cancellationToken))
        {
            logger.LogInformation("Mission {MissionCode} is already in the database, import skipped", mission.MissionCode);
            return false;
        }

        // One SaveChanges = one transaction: the mission, its sources, paths and events appear together or not at all.
        var planned = await DataSourceStore.EnsureAsync(
            db, DataSourceNames.PlannedTrajectoryFile, DataSourceType.PlannedFile, DataQuality.Unknown, cancellationToken);
        await DataSourceStore.EnsureAsync(
            db, DataSourceNames.InternalSimulator, DataSourceType.Simulator, DataQuality.Simulated, cancellationToken);

        var entity = MissionMapper.ToEntity(mission, isSimulated, timeProvider.GetUtcNow(), planned.Id);
        db.Missions.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent import of the same code loses the unique-index race: treat as "already there".
            await using var check = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (await check.Missions.AnyAsync(m => m.MissionCode == mission.MissionCode, cancellationToken))
            {
                logger.LogInformation(ex, "Mission {MissionCode} was imported concurrently, import skipped", mission.MissionCode);
                return false;
            }

            logger.LogError(ex, "Import of mission {MissionCode} failed", mission.MissionCode);
            throw;
        }

        logger.LogInformation(
            "Imported mission {MissionCode}: {VehicleCount} vehicles, {PointCount} points, {EventCount} events, T0 {LaunchTimeUtc:O}, simulated {IsSimulated}",
            mission.MissionCode,
            entity.Vehicles.Count,
            entity.Vehicles.Sum(v => v.Trajectories.Sum(t => t.Points.Count)),
            entity.Events.Count,
            entity.PlannedLaunchTimeUtc,
            isSimulated);
        return true;
    }

    public async Task<bool> SetPlannedLaunchTimeAsync(
        string missionCode, DateTimeOffset plannedLaunchTimeUtc, CancellationToken cancellationToken = default)
    {
        RequireUtc(plannedLaunchTimeUtc, nameof(plannedLaunchTimeUtc));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // One transaction: the new T0, the cleared simulation state and the deleted simulated samples appear together or
        // not at all, so no reader ever sees samples on a time axis that no longer exists.
        await using var transaction = await BeginWriteTransactionAsync(db, cancellationToken);
        var row = await db.Missions.FirstOrDefaultAsync(m => m.MissionCode == missionCode, cancellationToken);
        if (row is null)
        {
            logger.LogWarning("T0 change for unknown mission {MissionCode} ignored", missionCode);
            return false;
        }

        var old = row.PlannedLaunchTimeUtc;
        var oldAnchor = row.SimulationLaunchTimeUtc;
        var oldStatus = row.Status;
        row.PlannedLaunchTimeUtc = plannedLaunchTimeUtc;
        row.SimulationLaunchTimeUtc = null;
        row.ActualLaunchTimeUtc = null;
        row.Status = MissionStatus.Scheduled;
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var missionId = row.Id;
        var deleted = await db.TelemetrySamples
            .Where(s => s.Quality == DataQuality.Simulated && s.Vehicle.MissionId == missionId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Mission {MissionCode} T0 changed {OldLaunchTimeUtc:O} -> {NewLaunchTimeUtc:O}; status {OldStatus} -> Scheduled, simulation anchor {OldAnchorUtc:O} cleared, {Deleted} simulated samples deleted",
            missionCode, old, plannedLaunchTimeUtc, oldStatus, oldAnchor, deleted);
        return true;
    }

    public async Task<bool> BeginSimulationRunAsync(
        string missionCode, DateTimeOffset simulationLaunchTimeUtc, CancellationToken cancellationToken = default)
    {
        RequireUtc(simulationLaunchTimeUtc, nameof(simulationLaunchTimeUtc));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // One transaction: if anything fails the transaction is rolled back on dispose and the previous run (status, anchor,
        // samples) is intact. The planned T0 is not written: the run has its own time axis (the anchor).
        await using var transaction = await BeginWriteTransactionAsync(db, cancellationToken);
        var row = await db.Missions.FirstOrDefaultAsync(m => m.MissionCode == missionCode, cancellationToken);
        if (row is null)
        {
            logger.LogWarning("Simulation run start for unknown mission {MissionCode} ignored", missionCode);
            return false;
        }

        var oldStatus = row.Status;
        var oldAnchor = row.SimulationLaunchTimeUtc;
        row.Status = MissionStatus.Scheduled;
        row.SimulationLaunchTimeUtc = simulationLaunchTimeUtc;
        row.ActualLaunchTimeUtc = null;
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var missionId = row.Id;
        var deleted = await db.TelemetrySamples
            .Where(s => s.Quality == DataQuality.Simulated && s.Vehicle.MissionId == missionId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Mission {MissionCode} simulation run begun: status {OldStatus} -> Scheduled, simulation anchor {OldAnchorUtc:O} -> {NewAnchorUtc:O}, {Deleted} simulated samples deleted",
            missionCode, oldStatus, oldAnchor, simulationLaunchTimeUtc, deleted);
        return true;
    }

    /// <summary>
    /// Starts a write-locking transaction. A read followed by a write inside a deferred transaction can fail with
    /// SQLITE_BUSY_SNAPSHOT under WAL when another writer commits in between. EF's BeginTransactionAsync on
    /// Microsoft.Data.Sqlite is not deferred: it issues BEGIN IMMEDIATE, so the write lock is taken before the first read
    /// (verified by MissionRepositoryTransactionTests.EfTransaction_TakesTheWriteLockAtOnce); other writers wait for the
    /// busy timeout instead of failing later. Kept in one place so this stays true for every multi-step write.
    /// </summary>
    private static Task<IDbContextTransaction> BeginWriteTransactionAsync(StarshipDbContext db, CancellationToken cancellationToken) =>
        db.Database.BeginTransactionAsync(cancellationToken);

    public async Task<bool> SetFlightStateAsync(
        string missionCode,
        MissionStatus status,
        DateTimeOffset? actualLaunchTimeUtc,
        DateTimeOffset? simulationLaunchTimeUtc,
        bool clearActualLaunchTime = false,
        bool clearSimulationLaunchTime = false,
        CancellationToken cancellationToken = default)
    {
        if (actualLaunchTimeUtc is { } actual)
        {
            RequireUtc(actual, nameof(actualLaunchTimeUtc));
            RequireNotCleared(clearActualLaunchTime, nameof(actualLaunchTimeUtc), nameof(clearActualLaunchTime));
        }

        if (simulationLaunchTimeUtc is { } anchor)
        {
            RequireUtc(anchor, nameof(simulationLaunchTimeUtc));
            RequireNotCleared(clearSimulationLaunchTime, nameof(simulationLaunchTimeUtc), nameof(clearSimulationLaunchTime));
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Missions.FirstOrDefaultAsync(m => m.MissionCode == missionCode, cancellationToken);
        if (row is null)
        {
            logger.LogWarning("Flight state change for unknown mission {MissionCode} ignored", missionCode);
            return false;
        }

        var oldStatus = row.Status;
        row.Status = status;
        if (actualLaunchTimeUtc is not null || clearActualLaunchTime)
        {
            row.ActualLaunchTimeUtc = actualLaunchTimeUtc;
        }

        if (simulationLaunchTimeUtc is not null || clearSimulationLaunchTime)
        {
            row.SimulationLaunchTimeUtc = simulationLaunchTimeUtc;
        }

        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Mission {MissionCode} state {OldStatus} -> {Status}, actual {ActualLaunchTimeUtc:O}, simulation anchor {SimulationLaunchTimeUtc:O}, planned {PlannedLaunchTimeUtc:O}",
            missionCode, oldStatus, status, row.ActualLaunchTimeUtc, row.SimulationLaunchTimeUtc, row.PlannedLaunchTimeUtc);
        return true;
    }

    private static void RequireNotCleared(bool clear, string valueName, string flagName)
    {
        if (clear)
        {
            throw new ArgumentException($"'{valueName}' cannot be given together with '{flagName}' = true.", valueName);
        }
    }

    private static void RequireUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException($"'{paramName}' must be a UTC instant (offset zero), got offset {value.Offset}.", paramName);
        }
    }
}
