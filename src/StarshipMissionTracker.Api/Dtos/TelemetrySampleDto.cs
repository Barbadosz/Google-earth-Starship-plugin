using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Dtos;

/// <summary>
/// A stored telemetry sample. <c>offsetSeconds</c> = T+ relative to the mission's effective T0; <c>isEstimated</c> is
/// true for every quality below Published (Simulated samples are never presented as measured).
/// </summary>
public sealed record TelemetrySampleDto(
    string Vehicle,
    string TimestampUtc,
    double OffsetSeconds,
    string ReceivedAtUtc,
    double? Latitude,
    double? Longitude,
    double? AltitudeMeters,
    double? VelocityMetersPerSecond,
    double? HeadingDegrees,
    double? PitchDegrees,
    double? RollDegrees,
    string Quality,
    bool IsEstimated,
    string Source)
{
    public static TelemetrySampleDto From(TelemetrySample sample, DateTimeOffset launchTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return new TelemetrySampleDto(
            sample.Vehicle.ToString(),
            ApiTime.ToText(sample.TimestampUtc),
            (sample.TimestampUtc - launchTimeUtc).TotalSeconds,
            ApiTime.ToText(sample.ReceivedAtUtc),
            sample.Latitude,
            sample.Longitude,
            sample.AltitudeMeters,
            sample.VelocityMetersPerSecond,
            sample.HeadingDegrees,
            sample.PitchDegrees,
            sample.RollDegrees,
            sample.Quality.ToString(),
            sample.IsEstimated,
            sample.SourceName);
    }
}
