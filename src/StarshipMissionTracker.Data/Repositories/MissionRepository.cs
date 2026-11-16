using Microsoft.EntityFrameworkCore;
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
        return rows.Select(MissionMapper.ToInfo).ToList();
    }

    public async Task<MissionInfo?> GetInfoAsync(string missionCode, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Missions.AsNoTracking().FirstOrDefaultAsync(m => m.MissionCode == missionCode, cancellationToken);
        return row is null ? null : MissionMapper.ToInfo(row);
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
        return row is null ? null : MissionMapper.ToInfo(row);
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
        var row = await db.Missions.FirstOrDefaultAsync(m => m.MissionCode == missionCode, cancellationToken);
        if (row is null)
        {
            logger.LogWarning("T0 change for unknown mission {MissionCode} ignored", missionCode);
            return false;
        }

        var old = row.PlannedLaunchTimeUtc;
        row.PlannedLaunchTimeUtc = plannedLaunchTimeUtc;
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Mission {MissionCode} T0 changed {OldLaunchTimeUtc:O} -> {NewLaunchTimeUtc:O}", missionCode, old, plannedLaunchTimeUtc);
        return true;
    }

    public async Task<bool> SetFlightStateAsync(
        string missionCode,
        MissionStatus status,
        DateTimeOffset? actualLaunchTimeUtc,
        DateTimeOffset? plannedLaunchTimeUtc,
        CancellationToken cancellationToken = default)
    {
        if (actualLaunchTimeUtc is { } actual)
        {
            RequireUtc(actual, nameof(actualLaunchTimeUtc));
        }

        if (plannedLaunchTimeUtc is { } planned)
        {
            RequireUtc(planned, nameof(plannedLaunchTimeUtc));
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
        if (actualLaunchTimeUtc is not null)
        {
            row.ActualLaunchTimeUtc = actualLaunchTimeUtc;
        }

        if (plannedLaunchTimeUtc is not null)
        {
            row.PlannedLaunchTimeUtc = plannedLaunchTimeUtc.Value;
        }

        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Mission {MissionCode} state {OldStatus} -> {Status}, actual {ActualLaunchTimeUtc:O}, planned {PlannedLaunchTimeUtc:O}",
            missionCode, oldStatus, status, row.ActualLaunchTimeUtc, row.PlannedLaunchTimeUtc);
        return true;
    }

    private static void RequireUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException($"'{paramName}' must be a UTC instant (offset zero), got offset {value.Offset}.", paramName);
        }
    }
}
