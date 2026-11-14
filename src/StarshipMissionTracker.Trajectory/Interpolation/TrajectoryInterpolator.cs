using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Calculation;

namespace StarshipMissionTracker.Trajectory.Interpolation;

/// <summary>
/// Position at an arbitrary T+ offset: horizontal position along the WGS84 geodesic between the neighbouring source
/// points (constant ground speed within a segment), altitude, velocity and offset linear. Never extrapolates.
/// Interpolated points get quality <see cref="DataQuality.Estimated"/> (or the worse source quality, never better).
/// </summary>
public static class TrajectoryInterpolator
{
    /// <summary>
    /// Point at <paramref name="offsetSeconds"/>: the original point on an exact match, an interpolated (Estimated)
    /// point between neighbours, or null outside [first, last] offset (no extrapolation) or for an empty list.
    /// </summary>
    /// <exception cref="ArgumentException">Null element or offsets not strictly increasing.</exception>
    public static TrajectoryPoint? PointAt(IReadOnlyList<TrajectoryPoint> points, double offsetSeconds)
    {
        TrajectoryPointValidation.EnsureStrictlyIncreasing(points, nameof(points));
        if (!double.IsFinite(offsetSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(offsetSeconds), offsetSeconds, "Offset must be finite.");
        }

        if (points.Count == 0 || offsetSeconds < points[0].OffsetSeconds || offsetSeconds > points[^1].OffsetSeconds)
        {
            return null;
        }

        // Binary search for the first point with OffsetSeconds >= offsetSeconds.
        int low = 0, high = points.Count - 1;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (points[mid].OffsetSeconds < offsetSeconds)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        var after = points[low];
        if (after.OffsetSeconds == offsetSeconds)
        {
            return after;
        }

        var before = points[low - 1];
        var fraction = (offsetSeconds - before.OffsetSeconds) / (after.OffsetSeconds - before.OffsetSeconds);
        var segment = new GeodesicSegment(before.Latitude, before.Longitude, after.Latitude, after.Longitude);
        return Between(segment, before, after, fraction);
    }

    /// <summary>
    /// Interpolated point at <paramref name="fraction"/> (strictly between 0 and 1) of the segment from
    /// <paramref name="from"/> to <paramref name="to"/>; <paramref name="segment"/> must be the geodesic between them.
    /// Orientation is interpolated only where both ends have a value (heading and roll along the shorter arc).
    /// </summary>
    internal static TrajectoryPoint Between(
        GeodesicSegment segment, TrajectoryPoint from, TrajectoryPoint to, double fraction)
    {
        var (lat, lon, _) = segment.PositionAt(fraction);

        return new TrajectoryPoint(
            offsetSeconds: Lerp(from.OffsetSeconds, to.OffsetSeconds, fraction),
            latitude: lat,
            longitude: lon,
            altitudeMeters: Lerp(from.AltitudeMeters, to.AltitudeMeters, fraction),
            velocityMetersPerSecond: LerpOptional(from.VelocityMetersPerSecond, to.VelocityMetersPerSecond, fraction),
            quality: InterpolatedQuality(from.Quality, to.Quality),
            sourceName: from.SourceName == to.SourceName ? from.SourceName : $"{from.SourceName} / {to.SourceName}",
            headingDegrees: LerpHeading(from.HeadingDegrees, to.HeadingDegrees, fraction),
            pitchDegrees: LerpOptional(from.PitchDegrees, to.PitchDegrees, fraction),
            rollDegrees: LerpRoll(from.RollDegrees, to.RollDegrees, fraction));
    }

    /// <summary>Estimated, or the worse of the two source qualities if that is already worse (never upgrades).</summary>
    internal static DataQuality InterpolatedQuality(DataQuality a, DataQuality b) =>
        DataQualityExtensions.Worst(DataQuality.Estimated, DataQualityExtensions.Worst(a, b));

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static double? LerpOptional(double? a, double? b, double t) =>
        a is { } x && b is { } y ? Lerp(x, y, t) : null;

        private static double? LerpHeading(double? a, double? b, double t) =>
        ShortestArc(a, b, t) is { } v ? GeodesicCalculator.NormalizeAzimuth(v) : null;

    /// <summary>Roll along the shorter arc across the +/-180 wrap; result in [-180, 180).</summary>
    private static double? LerpRoll(double? a, double? b, double t) =>
        ShortestArc(a, b, t) is { } v ? (((((v + 180) % 360) + 360) % 360) - 180) : null;

    private static double? ShortestArc(double? a, double? b, double t)
    {
        if (a is not { } x || b is not { } y)
        {
            return null;
        }

        var delta = ((((y - x) % 360) + 540) % 360) - 180; // shortest signed difference in [-180, 180)
        return x + (delta * t);
    }
}
