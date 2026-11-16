using System.Collections.Concurrent;
using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Api.Services;

/// <summary>
/// Remembers which vehicles are currently reported STALE, so the live document logs the warning once per state change
/// (and once more per new latest sample) instead of on every poll. Singleton, thread-safe.
/// </summary>
public sealed class StaleTelemetryTracker
{
    private readonly ConcurrentDictionary<(string MissionCode, VehicleType Vehicle), DateTimeOffset> _stale = new();

    /// <summary>True when the vehicle was not stale before, or was stale with an older latest sample (log a warning).</summary>
    public bool MarkStale(string missionCode, VehicleType vehicle, DateTimeOffset latestSampleUtc)
    {
        ArgumentNullException.ThrowIfNull(missionCode);
        var key = (missionCode.ToUpperInvariant(), vehicle);
        var changed = true;
        _stale.AddOrUpdate(
            key,
            latestSampleUtc,
            (_, previous) =>
            {
                changed = previous != latestSampleUtc;
                return latestSampleUtc;
            });
        return changed;
    }

    /// <summary>True when the vehicle was stale until now (log the recovery).</summary>
    public bool Clear(string missionCode, VehicleType vehicle)
    {
        ArgumentNullException.ThrowIfNull(missionCode);
        return _stale.TryRemove((missionCode.ToUpperInvariant(), vehicle), out _);
    }
}
