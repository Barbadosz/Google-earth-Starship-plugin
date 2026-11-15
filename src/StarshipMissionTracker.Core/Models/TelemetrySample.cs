using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Core.Models;

/// <summary>
/// One telemetry sample of one vehicle. Timestamps are UTC (offset zero). Latitude and longitude are both set or both null;
/// altitude is metres above mean sea level (EGM96). Orientation ranges as in <see cref="TrajectoryPoint"/>.
/// Every sample carries a quality and a source name (never presented as measured unless it is).
/// </summary>
public sealed record TelemetrySample
{
    public TelemetrySample(
        VehicleType vehicle,
        DateTimeOffset timestampUtc,
        DateTimeOffset receivedAtUtc,
        double? latitude,
        double? longitude,
        double? altitudeMeters,
        double? velocityMetersPerSecond,
        double? headingDegrees,
        double? pitchDegrees,
        double? rollDegrees,
        DataQuality quality,
        string sourceName)
    {
        UtcCheck.Require(timestampUtc, nameof(timestampUtc));
        UtcCheck.Require(receivedAtUtc, nameof(receivedAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        if (latitude.HasValue != longitude.HasValue)
        {
            throw new ArgumentException("Latitude and longitude must both be set or both be null.", nameof(latitude));
        }

        if (latitude is { } lat)
        {
            Guard.Latitude(lat, nameof(latitude));
        }

        if (longitude is { } lon)
        {
            longitude = Guard.Longitude(lon, nameof(longitude));
        }

        if (altitudeMeters is { } alt)
        {
            Guard.Finite(alt, nameof(altitudeMeters));
        }

        if (velocityMetersPerSecond is { } v)
        {
            Guard.Finite(v, nameof(velocityMetersPerSecond));
        }

        if (headingDegrees is { } h)
        {
            Guard.Finite(h, nameof(headingDegrees));
            h %= 360;
            if (h < 0)
            {
                h += 360;
            }

            headingDegrees = h >= 360 ? 0 : h;
        }

        if (pitchDegrees is { } p && (!double.IsFinite(p) || p is < -90 or > 90))
        {
            throw new ArgumentOutOfRangeException(nameof(pitchDegrees), p, "Pitch must be within [-90, 90].");
        }

        if (rollDegrees is { } r && (!double.IsFinite(r) || r is < -180 or > 180))
        {
            throw new ArgumentOutOfRangeException(nameof(rollDegrees), r, "Roll must be within [-180, 180].");
        }

        Vehicle = vehicle;
        TimestampUtc = timestampUtc;
        ReceivedAtUtc = receivedAtUtc;
        Latitude = latitude;
        Longitude = longitude;
        AltitudeMeters = altitudeMeters;
        VelocityMetersPerSecond = velocityMetersPerSecond;
        HeadingDegrees = headingDegrees;
        PitchDegrees = pitchDegrees;
        RollDegrees = rollDegrees;
        Quality = quality;
        SourceName = sourceName;
    }

    public VehicleType Vehicle { get; }

    /// <summary>Time the sample describes (mission time axis, UTC).</summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>Wall-clock time the server received the sample (UTC); used for staleness.</summary>
    public DateTimeOffset ReceivedAtUtc { get; }

    public double? Latitude { get; }

    /// <summary>Degrees in [-180, 180); +180 is normalised to -180.</summary>
    public double? Longitude { get; }

    /// <summary>Metres above mean sea level (EGM96).</summary>
    public double? AltitudeMeters { get; }

    public double? VelocityMetersPerSecond { get; }

    /// <summary>Degrees clockwise from true north, normalised to [0, 360).</summary>
    public double? HeadingDegrees { get; }

    public double? PitchDegrees { get; }
    public double? RollDegrees { get; }
    public DataQuality Quality { get; }
    public string SourceName { get; }

    public bool HasPosition => Latitude.HasValue && Longitude.HasValue;
    public bool HasOrientation => HeadingDegrees.HasValue || PitchDegrees.HasValue || RollDegrees.HasValue;

    /// <summary>True unless the sample is Published or Measured.</summary>
    public bool IsEstimated => Quality is not (DataQuality.Published or DataQuality.Measured);
}
