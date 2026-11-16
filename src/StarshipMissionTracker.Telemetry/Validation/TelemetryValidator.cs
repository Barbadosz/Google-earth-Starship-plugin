using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Simulation;

namespace StarshipMissionTracker.Telemetry.Validation;

/// <summary>Outcome of validating one sample; <see cref="Reason"/> is null when valid.</summary>
public sealed record TelemetryValidationResult(bool IsValid, string? Reason)
{
    public static TelemetryValidationResult Ok { get; } = new(true, null);

    public static TelemetryValidationResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// Rules for incoming telemetry (reused by later real adapters): finite numbers, latitude/longitude range and pair rule,
/// strictly increasing timestamps per vehicle, Measured/Published forbidden for the simulator source,
/// future timestamps only for the simulator source.
/// </summary>
public static class TelemetryValidator
{
    public static readonly TimeSpan DefaultFutureTolerance = TimeSpan.FromSeconds(2);

    public static bool IsSimulatorSource(string sourceName) =>
        string.Equals(sourceName, SimulatorSource.Name, StringComparison.Ordinal);

    /// <param name="sample">The sample to check.</param>
    /// <param name="previousTimestampUtc">Timestamp of the previous accepted sample of the same vehicle, if any.</param>
    /// <param name="nowUtc">Wall-clock now (from the TimeProvider).</param>
    /// <param name="futureTolerance">Allowed lead of a non-simulator timestamp over <paramref name="nowUtc"/>.</param>
    public static TelemetryValidationResult Validate(
        TelemetrySample sample,
        DateTimeOffset? previousTimestampUtc,
        DateTimeOffset nowUtc,
        TimeSpan? futureTolerance = null)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (sample.Latitude.HasValue != sample.Longitude.HasValue)
        {
            return TelemetryValidationResult.Fail("latitude and longitude must both be set or both be null");
        }

        if (sample.Latitude is { } lat && (!double.IsFinite(lat) || lat is < -90 or > 90))
        {
            return TelemetryValidationResult.Fail($"latitude {lat} is out of range");
        }

        if (sample.Longitude is { } lon && (!double.IsFinite(lon) || lon is < -180 or > 180))
        {
            return TelemetryValidationResult.Fail($"longitude {lon} is out of range");
        }

        foreach (var (name, value) in Numbers(sample))
        {
            if (value is { } v && !double.IsFinite(v))
            {
                return TelemetryValidationResult.Fail($"{name} is not a finite number");
            }
        }

        if (previousTimestampUtc is { } previous && sample.TimestampUtc <= previous)
        {
            return TelemetryValidationResult.Fail(
                $"timestamp {sample.TimestampUtc:O} does not follow the previous {previous:O} of {sample.Vehicle}");
        }

        var simulator = IsSimulatorSource(sample.SourceName);
        if (simulator && sample.Quality is DataQuality.Measured or DataQuality.Published)
        {
            return TelemetryValidationResult.Fail($"the simulator source cannot produce {sample.Quality} data");
        }

        if (!simulator && sample.TimestampUtc > nowUtc + (futureTolerance ?? DefaultFutureTolerance))
        {
            return TelemetryValidationResult.Fail($"timestamp {sample.TimestampUtc:O} is in the future");
        }

        return TelemetryValidationResult.Ok;
    }

    private static IEnumerable<(string Name, double? Value)> Numbers(TelemetrySample s)
    {
        yield return (nameof(s.AltitudeMeters), s.AltitudeMeters);
        yield return (nameof(s.VelocityMetersPerSecond), s.VelocityMetersPerSecond);
        yield return (nameof(s.HeadingDegrees), s.HeadingDegrees);
        yield return (nameof(s.PitchDegrees), s.PitchDegrees);
        yield return (nameof(s.RollDegrees), s.RollDegrees);
    }
}
