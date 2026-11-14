using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Trajectory.Calculation;

/// <summary>
/// Orientation derived from the direction of travel between consecutive points: heading = WGS84 geodesic azimuth
/// (degrees clockwise from true north, = KML heading), pitch = atan2(altitude change, ground distance) in degrees
/// (positive = climbing), roll = 0 (no source for it). Derived values describe the path geometry, not measured
/// attitude, so any point that receives a derived angle gets quality Worst(Estimated, original quality): it is never
/// left looking Published/Measured with invented angles. Points that already have all angles are returned unchanged.
/// </summary>
public static class OrientationCalculator
{
    /// <summary>Below this ground distance (metres) the azimuth is treated as undefined.</summary>
    public const double MinHorizontalDistanceMeters = 1e-3;

    /// <summary>Below this altitude change (metres) a zero-ground-distance segment is treated as no motion.</summary>
    public const double MinVerticalDistanceMeters = 1e-3;

    /// <summary>Heading of travel from <paramref name="from"/> towards <paramref name="to"/>; null if no horizontal motion.</summary>
    public static double? HeadingDegrees(TrajectoryPoint from, TrajectoryPoint to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        var inverse = GeodesicCalculator.Inverse(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
        return inverse.DistanceMeters < MinHorizontalDistanceMeters ? null : inverse.InitialAzimuthDegrees;
    }

    /// <summary>Climb angle in [-90, 90]; +/-90 for a vertical-only segment; null if the points coincide.</summary>
    public static double? PitchDegrees(TrajectoryPoint from, TrajectoryPoint to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        var ground = GeodesicCalculator.Inverse(from.Latitude, from.Longitude, to.Latitude, to.Longitude).DistanceMeters;
        return Pitch(ground, to.AltitudeMeters - from.AltitudeMeters);
    }

    /// <summary>
    /// Returns a copy in which missing heading/pitch are derived from the outgoing segment (the last point uses the
    /// arrival direction of the final segment) and missing roll is 0. Existing values are kept. Where a segment has no
    /// horizontal motion (vertical climb) or no motion at all, the nearest defined value is reused: the previous one,
    /// or for a leading gap (e.g. vertical lift-off) the first later one. A point with any derived angle (incl. roll = 0)
    /// is downgraded to at most Estimated. Lists with fewer than 2 points are copied as is.
    /// </summary>
    /// <exception cref="ArgumentException">Null element or offsets not strictly increasing.</exception>
    public static IReadOnlyList<TrajectoryPoint> FillMissing(IReadOnlyList<TrajectoryPoint> points)
    {
        TrajectoryPointValidation.EnsureStrictlyIncreasing(points, nameof(points));
        if (points.Count < 2)
        {
            return points.ToArray();
        }

        var count = points.Count;
        var headings = new double?[count];
        var pitches = new double?[count];
        for (var i = 0; i < count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var inverse = GeodesicCalculator.Inverse(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            var horizontal = inverse.DistanceMeters >= MinHorizontalDistanceMeters;
            headings[i] = horizontal ? inverse.InitialAzimuthDegrees : null;
            pitches[i] = Pitch(inverse.DistanceMeters, b.AltitudeMeters - a.AltitudeMeters);

            if (i == count - 2)
            {
                headings[i + 1] = horizontal ? inverse.FinalAzimuthDegrees : null;
                pitches[i + 1] = pitches[i];
            }
        }

        FillGaps(headings);
        FillGaps(pitches);

        var result = new TrajectoryPoint[count];
        for (var i = 0; i < count; i++)
        {
            var p = points[i];
            var heading = p.HeadingDegrees ?? headings[i];
            var pitch = p.PitchDegrees ?? pitches[i];
            var roll = p.RollDegrees ?? (heading.HasValue || pitch.HasValue ? 0 : null);

            result[i] = heading == p.HeadingDegrees && pitch == p.PitchDegrees && roll == p.RollDegrees
                ? p
                : new TrajectoryPoint(
                    p.OffsetSeconds, p.Latitude, p.Longitude, p.AltitudeMeters, p.VelocityMetersPerSecond,
                    DataQualityExtensions.Worst(DataQuality.Estimated, p.Quality), p.SourceName, heading, pitch, roll);
        }

        return result;
    }

    private static double? Pitch(double groundMeters, double altitudeChangeMeters)
    {
        if (groundMeters < MinHorizontalDistanceMeters && Math.Abs(altitudeChangeMeters) < MinVerticalDistanceMeters)
        {
            return null;
        }

        var pitch = Math.Atan2(altitudeChangeMeters, groundMeters) * (180 / Math.PI);
        return Math.Clamp(pitch, -90, 90);
    }

    /// <summary>Forward-fills nulls from the previous value; leading nulls take the first defined value.</summary>
    private static void FillGaps(double?[] values)
    {
        var firstDefined = Array.FindIndex(values, v => v.HasValue);
        if (firstDefined < 0)
        {
            return;
        }

        for (var i = 0; i < firstDefined; i++)
        {
            values[i] = values[firstDefined];
        }

        for (var i = firstDefined + 1; i < values.Length; i++)
        {
            values[i] ??= values[i - 1];
        }
    }
}
