using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Interpolation;

namespace StarshipMissionTracker.Trajectory.Calculation;

/// <summary>
/// Densifies a trajectory along the WGS84 geodesic so KML straight segments follow the curvature of the Earth.
/// A segment is split into N equal parts (equal ground distance and equal time) where
/// N = max(ceil(groundDistance / MaxSpacingMeters), ceil(timeGap / MaxSpacingSeconds)), i.e. when EITHER limit is
/// exceeded. The distance limit uses the horizontal (ellipsoidal) distance; a vertical-only segment is split by time.
/// Original points are returned unchanged (same instances); inserted points are Estimated (see
/// <see cref="TrajectoryInterpolator"/>). A FlightPath built from the result must therefore have quality
/// Estimated or worse (Core rule).
/// </summary>
public sealed class TrajectoryProcessor : ITrajectoryProcessor
{
    /// <summary>Safety cap on the output size, against absurdly small limits.</summary>
    public const int MaxOutputPoints = 1_000_000;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Null element or offsets not strictly increasing.</exception>
    /// <exception cref="MissionDataException">Output over the cap (absurd input data or spacing limits).</exception>
    public IReadOnlyList<TrajectoryPoint> Densify(IReadOnlyList<TrajectoryPoint> points, TrajectoryDensificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        TrajectoryPointValidation.EnsureStrictlyIncreasing(points, nameof(points));

        if (points.Count < 2)
        {
            return points.ToArray();
        }

        var result = new List<TrajectoryPoint>(points.Count) { points[0] };
        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var duration = to.OffsetSeconds - from.OffsetSeconds;
            var segment = new GeodesicSegment(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
            var pieces = PieceCount(segment.DistanceMeters, duration, options);

            if (result.Count + pieces > MaxOutputPoints)
            {
                throw new MissionDataException(
                    $"Densification would exceed the output cap MaxOutputPoints = {MaxOutputPoints} points " +
                    $"(segment {i - 1}->{i}: {segment.DistanceMeters:F0} m / MaxSpacingMeters {options.MaxSpacingMeters}, " +
                    $"{duration} s / MaxSpacingSeconds {options.MaxSpacingSeconds}); the trajectory has too long a gap or the spacing limits are too small.");
            }

            for (var k = 1; k < pieces; k++)
            {
                result.Add(TrajectoryInterpolator.Between(segment, from, to, (double)k / pieces));
            }

            result.Add(to);
        }

        return result.ToArray();
    }

    private static long PieceCount(double distanceMeters, double durationSeconds, TrajectoryDensificationOptions options)
    {
        var byDistance = Math.Ceiling(distanceMeters / options.MaxSpacingMeters);
        var byTime = Math.Ceiling(durationSeconds / options.MaxSpacingSeconds);
        var pieces = Math.Max(1, Math.Max(byDistance, byTime));
        return pieces > MaxOutputPoints ? MaxOutputPoints + 1L : (long)pieces;
    }
}
