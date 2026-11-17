using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// N-20: time-synchronised Douglas-Peucker thinning of display lines (trail, planned/actual path, curtain).
/// <para>
/// Only drops points: the result is a subset of the input, same order, same instances (no point is moved,
/// added or re-timed, so no telemetry is fabricated). First and last points are always kept.
/// </para>
/// <para>
/// Error of a candidate point = 3D distance in metres (WGS84 Earth-centred Cartesian frame) between the point and the
/// position interpolated <em>in time</em> on the straight segment between the kept neighbours. Google Earth draws an
/// absolute-altitude line as straight 3D segments between vertices (they can cut below the curved surface, which is why
/// paths are densified), so this bounds what the user sees: the thinned line never departs more than the tolerance from
/// the drawn full-resolution line, and densification points needed for Earth curvature are kept. The time
/// synchronisation also keeps points where the speed changes along a straight line. No degree arithmetic, so it is safe
/// across the antimeridian and at the poles.
/// </para>
/// </summary>
public static class TrackThinner
{
    /// <summary>Paths with at most this many points are returned unchanged (plan N-20).</summary>
    public const int MinPointsToThin = 4;

    // WGS84 ellipsoid.
    private const double SemiMajorAxis = 6378137.0;
    private const double Flattening = 1 / 298.257223563;
    private const double EccentricitySquared = Flattening * (2 - Flattening);

    /// <summary>
    /// Thinned copy of <paramref name="points"/>: every dropped point lies within <paramref name="toleranceMeters"/>
    /// of the time-interpolated position between its kept neighbours. Lists with fewer than
    /// <see cref="MinPointsToThin"/> points come back unchanged (as a new list).
    /// </summary>
    /// <exception cref="ArgumentNullException">The list is null.</exception>
    /// <exception cref="ArgumentException">A null element, or offsets not strictly increasing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Tolerance negative or not finite.</exception>
    public static IReadOnlyList<TrajectoryPoint> Thin(IReadOnlyList<TrajectoryPoint> points, double toleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!double.IsFinite(toleranceMeters) || toleranceMeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toleranceMeters), toleranceMeters, "Tolerance must be finite and not negative.");
        }

        EnsureStrictlyIncreasing(points);

        if (points.Count < MinPointsToThin)
        {
            return points.ToArray();
        }

        var cartesian = new (double X, double Y, double Z)[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            cartesian[i] = ToCartesian(points[i].Latitude, points[i].Longitude, points[i].AltitudeMeters);
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        // Iterative (explicit stack) so long paths cannot overflow the call stack.
        var pending = new Stack<(int Start, int End)>();
        pending.Push((0, points.Count - 1));
        while (pending.Count > 0)
        {
            var (start, end) = pending.Pop();
            if (end - start < 2)
            {
                continue;
            }

            var (worstIndex, worstError) = WorstPoint(points, cartesian, start, end);
            if (worstError > toleranceMeters)
            {
                keep[worstIndex] = true;
                pending.Push((worstIndex, end));
                pending.Push((start, worstIndex));
            }
        }

        var result = new List<TrajectoryPoint>();
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Distance in metres between <paramref name="point"/> and the position interpolated at its time on the straight 3D
    /// segment (Earth-centred Cartesian) from <paramref name="from"/> to <paramref name="to"/>: the segment Google Earth
    /// draws between two kept vertices of an absolute-altitude line.
    /// </summary>
    internal static double TimeSyncedError(TrajectoryPoint from, TrajectoryPoint to, TrajectoryPoint point) =>
        Error(
            from.OffsetSeconds,
            ToCartesian(from.Latitude, from.Longitude, from.AltitudeMeters),
            to.OffsetSeconds,
            ToCartesian(to.Latitude, to.Longitude, to.AltitudeMeters),
            point.OffsetSeconds,
            ToCartesian(point.Latitude, point.Longitude, point.AltitudeMeters));

    private static (int Index, double Error) WorstPoint(
        IReadOnlyList<TrajectoryPoint> points, (double X, double Y, double Z)[] cartesian, int start, int end)
    {
        var worstIndex = start + 1;
        var worstError = -1.0;
        for (var i = start + 1; i < end; i++)
        {
            var error = Error(
                points[start].OffsetSeconds, cartesian[start], points[end].OffsetSeconds, cartesian[end],
                points[i].OffsetSeconds, cartesian[i]);

            // Strictly greater: the first of equal maxima wins (deterministic, keeps the result idempotent).
            if (error > worstError)
            {
                worstError = error;
                worstIndex = i;
            }
        }

        return (worstIndex, worstError);
    }

    private static double Error(
        double fromSeconds,
        (double X, double Y, double Z) from,
        double toSeconds,
        (double X, double Y, double Z) to,
        double pointSeconds,
        (double X, double Y, double Z) point)
    {
        var fraction = Math.Clamp((pointSeconds - fromSeconds) / (toSeconds - fromSeconds), 0, 1);
        var dx = point.X - (from.X + ((to.X - from.X) * fraction));
        var dy = point.Y - (from.Y + ((to.Y - from.Y) * fraction));
        var dz = point.Z - (from.Z + ((to.Z - from.Z) * fraction));
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>Geodetic (WGS84 degrees, ellipsoidal height in metres) to Earth-centred Cartesian metres.</summary>
    private static (double X, double Y, double Z) ToCartesian(double latitude, double longitude, double altitudeMeters)
    {
        var phi = latitude * Math.PI / 180;
        var lambda = longitude * Math.PI / 180;
        var sinPhi = Math.Sin(phi);
        var cosPhi = Math.Cos(phi);
        var primeVertical = SemiMajorAxis / Math.Sqrt(1 - (EccentricitySquared * sinPhi * sinPhi));

        return (
            (primeVertical + altitudeMeters) * cosPhi * Math.Cos(lambda),
            (primeVertical + altitudeMeters) * cosPhi * Math.Sin(lambda),
            ((primeVertical * (1 - EccentricitySquared)) + altitudeMeters) * sinPhi);
    }

    private static void EnsureStrictlyIncreasing(IReadOnlyList<TrajectoryPoint> points)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i] is null)
            {
                throw new ArgumentException($"Point at index {i} is null.", nameof(points));
            }

            if (i > 0 && points[i].OffsetSeconds <= points[i - 1].OffsetSeconds)
            {
                throw new ArgumentException(
                    $"Offsets must be strictly increasing: index {i} has T+{points[i].OffsetSeconds} s " +
                    $"after T+{points[i - 1].OffsetSeconds} s.",
                    nameof(points));
            }
        }
    }
}
