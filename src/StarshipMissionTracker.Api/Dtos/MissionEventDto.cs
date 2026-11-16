using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Dtos;

/// <summary>A planned flight event at T0 + offset (T0 = the mission clock, i.e. the effective T0).</summary>
public sealed record MissionEventDto(
    string Type,
    string Name,
    string? Description,
    double OffsetSeconds,
    string TimeUtc,
    string? Vehicle,
    double? Latitude,
    double? Longitude,
    double? AltitudeMeters,
    string Quality,
    string Source)
{
    public static MissionEventDto From(MissionEvent missionEvent, LaunchClock clock)
    {
        ArgumentNullException.ThrowIfNull(missionEvent);
        ArgumentNullException.ThrowIfNull(clock);
        return new MissionEventDto(
            missionEvent.Type.ToString(),
            missionEvent.Name,
            missionEvent.Description,
            missionEvent.OffsetSeconds,
            ApiTime.ToText(clock.ToUtc(missionEvent.OffsetSeconds)),
            missionEvent.Vehicle?.ToString(),
            missionEvent.Latitude,
            missionEvent.Longitude,
            missionEvent.AltitudeMeters,
            missionEvent.Quality.ToString(),
            missionEvent.SourceName);
    }
}
