using System.Collections.Concurrent;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Calculation;

namespace StarshipMissionTracker.Api.Services;

/// <summary>A mission with every path prepared for KML (densified, orientation filled) and the set of densified paths.</summary>
public sealed record PreparedMission(Mission Mission, IReadOnlySet<FlightPath> DensifiedPaths);

/// <summary>
/// Caches the expensive part of the planned document per mission: loading the planned mission and densifying its paths.
/// An entry is valid for one <c>(code, UpdatedAtUtc)</c>; the effective T0 is part of the key as well, so any T0 change
/// (planned edit or simulation anchor) rebuilds it. The KML string itself is not cached: each response needs a new
/// NetworkLinkControl revision and the request's host in the URLs. Singleton, thread-safe; a concurrent miss may
/// prepare twice (the last one wins), which is harmless.
/// </summary>
public sealed class PlannedDocumentCache(FlightPathPreparer preparer, ILogger<PlannedDocumentCache> logger)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the cached entry for <paramref name="info"/>, or prepares <paramref name="load"/>'s mission and caches it.
    /// Null when the loader returns null (mission vanished).
    /// </summary>
    /// <exception cref="MissionChangedException">The loaded mission's clock does not match the info's effective T0.</exception>
    public async Task<PreparedMission?> GetOrCreateAsync(MissionInfo info, Func<CancellationToken, Task<Mission?>> load, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(load);
        var key = new Key(info.UpdatedAtUtc, info.LaunchTimeUtc);
        if (_entries.TryGetValue(info.MissionCode, out var hit) && hit.Key == key)
        {
            return hit.Value;
        }

        var mission = await load(cancellationToken).ConfigureAwait(false);
        if (mission is null)
        {
            return null;
        }

        if (mission.Clock.LaunchTimeUtc != info.LaunchTimeUtc)
        {
            // The T0 changed between reading the header and the mission; the caller retries with a fresh header.
            throw new MissionChangedException(
                $"Mission '{info.MissionCode}' changed while loading (T0 {mission.Clock.LaunchTimeUtc:O} vs {info.LaunchTimeUtc:O}).");
        }

        var prepared = Prepare(mission);
        _entries[info.MissionCode] = new Entry(key, prepared);
        logger.LogInformation(
            "Planned mission {MissionCode} prepared for KML (T0 {LaunchTimeUtc:O}, updated {UpdatedAtUtc:O}, {Densified} densified paths)",
            info.MissionCode, info.LaunchTimeUtc, info.UpdatedAtUtc, prepared.DensifiedPaths.Count);
        return prepared;
    }

    /// <summary>Drops the entry of a mission (after a T0 change through the API).</summary>
    public void Invalidate(string missionCode)
    {
        ArgumentNullException.ThrowIfNull(missionCode);
        if (_entries.TryRemove(missionCode, out _))
        {
            logger.LogDebug("Planned cache entry of {MissionCode} invalidated", missionCode);
        }
    }

    private PreparedMission Prepare(Mission mission)
    {
        var densified = new HashSet<FlightPath>(ReferenceEqualityComparer.Instance);
        var vehicles = mission.Vehicles
            .Select(v => new Vehicle(v.Name, v.Type, v.ModelPath, v.FlightPaths.Select(p =>
            {
                var prepared = preparer.Prepare(p);
                if (prepared.Points.Count > p.Points.Count)
                {
                    densified.Add(prepared);
                }

                return prepared;
            }).ToList()))
            .ToList();
        return new PreparedMission(new Mission(mission.Config, mission.Clock, vehicles), densified);
    }

    private readonly record struct Key(DateTimeOffset UpdatedAtUtc, DateTimeOffset LaunchTimeUtc);

    private sealed record Entry(Key Key, PreparedMission Value);
}
