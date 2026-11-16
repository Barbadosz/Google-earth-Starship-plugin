using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Data.Mapping;

namespace StarshipMissionTracker.Data.Repositories;

public sealed class TelemetryRepository(
    IDbContextFactory<StarshipDbContext> contextFactory,
    ILogger<TelemetryRepository> logger) : ITelemetryRepository
{
    public async Task AddRangeAsync(string missionCode, IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var vehicleIds = await VehicleIdsAsync(db, missionCode, cancellationToken);

        var missionIsSimulated = await db.Missions.AsNoTracking()
            .Where(m => m.MissionCode == missionCode).Select(m => m.IsSimulated).FirstAsync(cancellationToken);

        // Provenance rules, checked for EVERY sample against its source row (nothing is saved before all pass, so a bad batch stores nothing):
        // - a source must be registered; only DataSourceNames.InternalSimulator may be created on the fly (M2 has no other producer);
        // - a Simulator source only yields Simulated samples;
        // - a sample is never more reliable than its source (DataQualityExtensions.Rank: Unknown < Simulated < Estimated < Reconstructed < Published < Measured);
        // - a simulated mission never holds Measured samples.
        var sources = new Dictionary<string, (Guid Id, DataSourceType Type, DataQuality Reliability)>(StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            if (!vehicleIds.TryGetValue(sample.Vehicle, out var vehicleId))
            {
                throw new InvalidOperationException($"Mission '{missionCode}' has no {sample.Vehicle} vehicle; the sample cannot be stored.");
            }

            if (!sources.TryGetValue(sample.SourceName, out var source))
            {
                if (sample.SourceName != DataSourceNames.InternalSimulator
                    && !await db.DataSources.AnyAsync(s => s.Name == sample.SourceName, cancellationToken))
                {
                    throw Rejected(sample, "unknown data source (register it before storing its data)");
                }

                var entity = await DataSourceStore.EnsureAsync(
                    db, sample.SourceName, DataSourceType.Simulator, DataQuality.Simulated, cancellationToken);

                source = (entity.Id, entity.Type, entity.Reliability);
                sources[sample.SourceName] = source;
            }

            if (source.Type == DataSourceType.Simulator && sample.Quality != DataQuality.Simulated)
            {
                throw Rejected(sample, "a simulator source can only produce Simulated samples");
            }

            if (sample.Quality.IsBetterThan(source.Reliability))
            {
                throw Rejected(sample, $"quality is better than the source's reliability ({source.Reliability})");
            }

            if (missionIsSimulated && sample.Quality == DataQuality.Measured)
            {
                throw Rejected(sample, $"mission '{missionCode}' is simulated and cannot hold Measured data");
            }

            db.TelemetrySamples.Add(TelemetryMapper.ToEntity(sample, vehicleId, source.Id));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Storing {Count} telemetry samples of mission {MissionCode} failed", samples.Count, missionCode);
            throw;
        }

        logger.LogDebug("Stored {Count} telemetry samples for mission {MissionCode}", samples.Count, missionCode);
    }

    private static InvalidOperationException Rejected(TelemetrySample sample, string reason) =>
        new($"Telemetry sample rejected ({sample.Vehicle} at {sample.TimestampUtc:O}, source '{sample.SourceName}', quality {sample.Quality}): {reason}.");

    public async Task<IReadOnlyList<TelemetrySample>> GetLatestAsync(string missionCode, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var vehicleIds = await VehicleIdsAsync(db, missionCode, cancellationToken);
        var result = new List<TelemetrySample>();
        foreach (var (type, vehicleId) in vehicleIds.OrderBy(kv => kv.Key))
        {
            var latest = await db.TelemetrySamples.AsNoTracking()
                .Include(s => s.Vehicle).Include(s => s.Source)
                .Where(s => s.VehicleId == vehicleId)
                .OrderByDescending(s => s.TimestampUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (latest is not null)
            {
                result.Add(TelemetryMapper.ToModel(latest));
            }
            else
            {
                logger.LogDebug("No telemetry yet for {Vehicle} of mission {MissionCode}", type, missionCode);
            }
        }

        return result;
    }

    public async Task<FlightPath?> GetHistoryAsync(
        string missionCode, VehicleType vehicle, int maxPoints, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoints, 2);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // One context and one read transaction: T0 and the samples come from the same database snapshot even while the simulator writes.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var mission = await db.Missions.AsNoTracking()
            .Where(m => m.MissionCode == missionCode)
            .Select(m => new { m.PlannedLaunchTimeUtc, VehicleId = m.Vehicles.Where(v => v.Type == vehicle).Select(v => (Guid?)v.Id).FirstOrDefault() })
            .FirstOrDefaultAsync(cancellationToken);
        if (mission?.VehicleId is not { } vehicleId)
        {
            return null;
        }

        // Only samples that can form a trajectory point (full position incl. altitude); the offset axis is the mission T0.
        var rows = await db.TelemetrySamples.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.Latitude != null && s.Longitude != null && s.AltitudeMeters != null)
            .OrderBy(s => s.TimestampUtc)
            .Select(s => new HistoryRow(
                s.TimestampUtc, s.Latitude!.Value, s.Longitude!.Value, s.AltitudeMeters!.Value,
                s.VelocityMps, s.HeadingDegrees, s.PitchDegrees, s.RollDegrees, s.Quality, s.Source.Name))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var quality = rows.Select(r => r.Quality).Aggregate(DataQualityExtensions.Worst);
        var sourceName = string.Join(", ", rows.Select(r => r.SourceName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var points = Decimate(rows, maxPoints)
            .Select(r => new TrajectoryPoint(
                (r.TimestampUtc - mission.PlannedLaunchTimeUtc).TotalSeconds,
                r.Latitude, r.Longitude, r.AltitudeMeters, r.VelocityMps, r.Quality, r.SourceName,
                r.HeadingDegrees, r.PitchDegrees, r.RollDegrees))
            .ToList();

        logger.LogDebug(
            "History of {Vehicle} in {MissionCode}: {Rows} rows -> {Points} points, quality {Quality}", vehicle, missionCode, rows.Count, points.Count, quality);
        return new FlightPath(TrajectoryType.Actual, quality, sourceName, null, points);
    }

    /// <summary>
    /// Returns the oldest <paramref name="limit"/> samples (ordered by timestamp, then id) at or after <paramref name="since"/> (inclusive).
    /// The caller must cap <paramref name="limit"/>; the repository does not.
    /// </summary>
    public async Task<IReadOnlyList<TelemetrySample>> GetSamplesAsync(
        string missionCode,
        VehicleType? vehicle,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.TelemetrySamples.AsNoTracking()
            .Include(s => s.Vehicle).Include(s => s.Source)
            .Where(s => s.Vehicle.Mission.MissionCode == missionCode);
        if (vehicle is { } type)
        {
            query = query.Where(s => s.Vehicle.Type == type);
        }

        if (since is { } from)
        {
            // Inclusive: a sample at exactly "since" is returned.
            query = query.Where(s => s.TimestampUtc >= from);
        }

        var rows = await query.OrderBy(s => s.TimestampUtc).ThenBy(s => s.Id).Take(limit).ToListAsync(cancellationToken);
        return rows.Select(TelemetryMapper.ToModel).ToList();
    }

    public async Task<int> DeleteSimulatedAsync(string missionCode, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.TelemetrySamples
            .Where(s => s.Quality == DataQuality.Simulated && s.Vehicle.Mission.MissionCode == missionCode)
            .ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("Deleted {Count} simulated telemetry samples of mission {MissionCode}", deleted, missionCode);
        return deleted;
    }

    private static async Task<Dictionary<VehicleType, Guid>> VehicleIdsAsync(
        StarshipDbContext db, string missionCode, CancellationToken cancellationToken)
    {
        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(v => v.Mission.MissionCode == missionCode)
            .Select(v => new { v.Type, v.Id })
            .ToListAsync(cancellationToken);
        if (vehicles.Count == 0)
        {
            var exists = await db.Missions.AnyAsync(m => m.MissionCode == missionCode, cancellationToken);
            if (!exists)
            {
                throw new InvalidOperationException($"Unknown mission '{missionCode}'.");
            }
        }

        return vehicles.ToDictionary(v => v.Type, v => v.Id);
    }

    /// <summary>At most <paramref name="maxPoints"/> evenly spread rows; the first and the last are always kept, nothing is inserted.</summary>
    private static IEnumerable<HistoryRow> Decimate(List<HistoryRow> rows, int maxPoints)
    {
        if (rows.Count <= maxPoints)
        {
            return rows;
        }

        var picked = new List<HistoryRow>(maxPoints);
        for (var i = 0; i < maxPoints; i++)
        {
            picked.Add(rows[(int)Math.Round(i * (rows.Count - 1d) / (maxPoints - 1))]);
        }

        return picked;
    }

    private sealed record HistoryRow(
        DateTimeOffset TimestampUtc,
        double Latitude,
        double Longitude,
        double AltitudeMeters,
        double? VelocityMps,
        double? HeadingDegrees,
        double? PitchDegrees,
        double? RollDegrees,
        DataQuality Quality,
        string SourceName);
}
