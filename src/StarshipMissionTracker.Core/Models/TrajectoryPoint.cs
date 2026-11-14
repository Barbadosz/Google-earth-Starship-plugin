using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Core.Models;

/// <summary>
/// One trajectory sample. Time is a T+ offset in seconds from launch (negative = before launch).
/// Orientation (optional, for KML Model Orientation / gx:angles): heading in [0, 360) (normalised),
/// pitch in [-90, 90], roll in [-180, 180], all in degrees; all-null means unknown.
/// </summary>
public sealed record TrajectoryPoint
{
    public TrajectoryPoint(
        double offsetSeconds,
        double latitude,
        double longitude,
        double altitudeMeters,
        double? velocityMetersPerSecond,
        DataQuality quality,
        string sourceName,
        double? headingDegrees = null,
        double? pitchDegrees = null,
        double? rollDegrees = null)
    {
        Guard.Finite(offsetSeconds, nameof(offsetSeconds));
        Guard.Latitude(latitude, nameof(latitude));
        var lon = Guard.Longitude(longitude, nameof(longitude));
        Guard.Finite(altitudeMeters, nameof(altitudeMeters));

        if (velocityMetersPerSecond is { } v)
        {
            Guard.Finite(v, nameof(velocityMetersPerSecond));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

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

        OffsetSeconds = offsetSeconds;
        Latitude = latitude;
        Longitude = lon;
        AltitudeMeters = altitudeMeters;
        VelocityMetersPerSecond = velocityMetersPerSecond;
        Quality = quality;
        SourceName = sourceName;
        HeadingDegrees = headingDegrees;
        PitchDegrees = pitchDegrees;
        RollDegrees = rollDegrees;
    }

    public double OffsetSeconds { get; }
    public double Latitude { get; }

    /// <summary>Degrees in [-180, 180); an input of +180 is normalised to -180 (antimeridian rule).</summary>
    public double Longitude { get; }

    /// <summary>
    /// Metres above mean sea level (EGM96 geoid), matching KML altitudeMode "absolute" (see ai/KONVENCIOK.md).
    /// Ellipsoidal heights must be converted before constructing the point.
    /// </summary>
    public double AltitudeMeters { get; }

    public double? VelocityMetersPerSecond { get; }
    public DataQuality Quality { get; }
    public string SourceName { get; }

    /// <summary>Degrees clockwise from true north, normalised to [0, 360); null = unknown.</summary>
    public double? HeadingDegrees { get; }

    /// <summary>Degrees, [-90, 90]; null = unknown.</summary>
    public double? PitchDegrees { get; }

    /// <summary>Degrees, [-180, 180]; null = unknown.</summary>
    public double? RollDegrees { get; }

    public bool HasOrientation => HeadingDegrees.HasValue || PitchDegrees.HasValue || RollDegrees.HasValue;

    /// <summary>True unless the point is Published or Measured.</summary>
    public bool IsEstimated => Quality is not (DataQuality.Published or DataQuality.Measured);
}
