using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Dtos;

/// <summary>One stored (source, not densified) path of a vehicle; every point carries quality and source.</summary>
public sealed record TrajectoryDto(
    string Vehicle,
    string VehicleName,
    string Type,
    string Quality,
    string Source,
    string? License,
    IReadOnlyList<TrajectoryPointDto> Points)
{
    public static TrajectoryDto From(Vehicle vehicle, FlightPath path, LaunchClock clock)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(clock);
        return new TrajectoryDto(
            vehicle.Type.ToString(),
            vehicle.Name,
            path.Type.ToString(),
            path.Quality.ToString(),
            path.SourceName,
            path.License,
            path.Points.Select(p => TrajectoryPointDto.From(p, clock)).ToList());
    }
}

/// <summary>A path point: T+ offset, absolute UTC (T0 + offset), MSL altitude, optional velocity/orientation.</summary>
public sealed record TrajectoryPointDto(
    double OffsetSeconds,
    string TimeUtc,
    double Latitude,
    double Longitude,
    double AltitudeMeters,
    double? VelocityMetersPerSecond,
    double? HeadingDegrees,
    double? PitchDegrees,
    double? RollDegrees,
    string Quality,
    string Source)
{
    public static TrajectoryPointDto From(TrajectoryPoint point, LaunchClock clock) => new(
        point.OffsetSeconds,
        ApiTime.ToText(clock.ToUtc(point.OffsetSeconds)),
        point.Latitude,
        point.Longitude,
        point.AltitudeMeters,
        point.VelocityMetersPerSecond,
        point.HeadingDegrees,
        point.PitchDegrees,
        point.RollDegrees,
        point.Quality.ToString(),
        point.SourceName);
}
