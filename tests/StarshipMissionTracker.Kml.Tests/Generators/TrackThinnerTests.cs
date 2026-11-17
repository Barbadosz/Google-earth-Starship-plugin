using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Trajectory.Calculation;

namespace StarshipMissionTracker.Kml.Tests.Generators;

/// <summary>N-20 package B: time-synchronised Douglas-Peucker thinning of display lines.</summary>
public class TrackThinnerTests
{
    private const double Tol = KmlLod.TrailToleranceMeters;

    // Boca Chica pad, roughly.
    private const double PadLat = 25.997;
    private const double PadLon = -97.157;

    private static TrajectoryPoint Pt(double t, double lat, double lon, double alt) =>
        new(t, lat, lon, alt, null, DataQuality.Reconstructed, "test source");

    /// <summary>Points along one geodesic: distance(t) and altitude(t) given by the callers.</summary>
    private static TrajectoryPoint[] AlongGeodesic(
        double lat, double lon, double azimuth, int count, double dt, Func<double, double> distance, Func<double, double> altitude) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var t = i * dt;
            var (la, lo) = GeodesicCalculator.Direct(lat, lon, azimuth, distance(t));
            return Pt(t, la, lo, altitude(t));
        }).ToArray();

    /// <summary>Demo-like ascent: pad hold, accelerating, turning, climbing; 1 s steps.</summary>
    private static TrajectoryPoint[] CurvedAscent(int count = 600)
    {
        var points = new List<TrajectoryPoint>();
        double lat = PadLat, lon = PadLon, alt = 0;
        for (var i = 0; i < count; i++)
        {
            double t = i;
            points.Add(Pt(t, lat, lon, alt));
            var speed = t < 5 ? 0 : Math.Min(7000, 2 * (t - 5) + (0.02 * (t - 5) * (t - 5)));
            var azimuth = 90 + (10 * Math.Sin(t / 120));
            (lat, lon) = GeodesicCalculator.Direct(lat, lon, azimuth, speed * 0.6);
            alt += Math.Max(0, speed * 0.8 * Math.Cos(t / 400));
        }

        return [.. points];
    }

    /// <summary>
    /// Independent check of the bound: the dropped point against the straight 3D segment Google Earth draws between the
    /// kept neighbours, at the point's time (own ECEF conversion below, not the thinner's).
    /// </summary>
    private static void AssertDroppedWithinTolerance(IReadOnlyList<TrajectoryPoint> source, IReadOnlyList<TrajectoryPoint> kept)
    {
        var keptSet = kept.ToHashSet(ReferenceEqualityComparer.Instance);
        var k = 0;
        foreach (var p in source)
        {
            if (keptSet.Contains(p))
            {
                k++;
                continue;
            }

            // Kept neighbours of this dropped point and the thinner's own metric.
            var from = kept[k - 1];
            var to = kept[k];
            Assert.True(from.OffsetSeconds < p.OffsetSeconds && p.OffsetSeconds < to.OffsetSeconds);
            var error = TrackThinner.TimeSyncedError(from, to, p);
            Assert.True(error <= Tol, $"T+{p.OffsetSeconds}: {error} m > {Tol} m");

            var independent = ChordDistance(from, to, p);
            Assert.True(independent <= Tol * 1.01, $"T+{p.OffsetSeconds}: independent {independent} m > {Tol} m");
        }
    }

    /// <summary>Distance from <paramref name="p"/> to the time-interpolated point of the straight ECEF segment a-b.</summary>
    private static double ChordDistance(TrajectoryPoint a, TrajectoryPoint b, TrajectoryPoint p)
    {
        var f = (p.OffsetSeconds - a.OffsetSeconds) / (b.OffsetSeconds - a.OffsetSeconds);
        var (ea, eb, ep) = (Ecef(a), Ecef(b), Ecef(p));
        var expected = new double[3];
        for (var i = 0; i < 3; i++)
        {
            expected[i] = ea[i] + ((eb[i] - ea[i]) * f);
        }

        return Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(ep[i] - expected[i], 2)));
    }

    private static double[] Ecef(TrajectoryPoint p)
    {
        const double a = 6378137.0;
        const double e2 = 0.00669437999014; // WGS84 first eccentricity squared
        var lat = p.Latitude * Math.PI / 180;
        var lon = p.Longitude * Math.PI / 180;
        var n = a / Math.Sqrt(1 - (e2 * Math.Sin(lat) * Math.Sin(lat)));
        return
        [
            (n + p.AltitudeMeters) * Math.Cos(lat) * Math.Cos(lon),
            (n + p.AltitudeMeters) * Math.Cos(lat) * Math.Sin(lon),
            ((n * (1 - e2)) + p.AltitudeMeters) * Math.Sin(lat),
        ];
    }

    private static void AssertOrderedSubset(IReadOnlyList<TrajectoryPoint> source, IReadOnlyList<TrajectoryPoint> kept)
    {
        var j = 0;
        foreach (var p in kept)
        {
            while (j < source.Count && !ReferenceEquals(source[j], p))
            {
                j++;
            }

            Assert.True(j < source.Count, $"Kept point T+{p.OffsetSeconds} is not a source instance (or out of order).");
            j++;
        }
    }

    [Fact]
    public void Tolerance_IsEightMetres() => Assert.Equal(8.0, KmlLod.TrailToleranceMeters);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Thin_ThreeOrFewerPoints_ReturnedUnchanged(int count)
    {
        // Wildly non-linear on purpose: even so nothing is dropped.
        var source = Enumerable.Range(0, count).Select(i => Pt(i, PadLat + i, PadLon, i * 1000)).ToArray();

        var result = TrackThinner.Thin(source, Tol);

        Assert.Equal(count, result.Count);
        for (var i = 0; i < count; i++)
        {
            Assert.Same(source[i], result[i]);
        }
    }

    [Fact]
    public void Thin_ConstantSpeedStraightLine_CollapsesToEndpoints()
    {
        // Short (1 km) at constant speed and climb rate: Earth curvature sag over it is about 2 cm.
        var source = AlongGeodesic(PadLat, PadLon, 95, 500, 1, t => 2 * t, t => 0.5 * t);

        var result = TrackThinner.Thin(source, Tol);

        Assert.Equal(2, result.Count);
        Assert.Same(source[0], result[0]);
        Assert.Same(source[^1], result[^1]);
    }

    [Fact]
    public void Thin_ConstantSpeedVerticalClimb_CollapsesToEndpoints()
    {
        var source = Enumerable.Range(0, 100).Select(i => Pt(i, PadLat, PadLon, 100 * i)).ToArray();

        Assert.Equal([source[0], source[^1]], TrackThinner.Thin(source, Tol));
    }

    [Fact]
    public void Thin_LongGeodesicAtAltitude_KeepsPointsForEarthCurvature()
    {
        // Densified-like path: 10 km spacing along a 500 km geodesic at constant altitude and speed. Google Earth draws
        // straight 3D segments, so dropping a point would cut a chord below the curved path (sag of a 20 km chord ~8 m).
        var source = AlongGeodesic(PadLat, PadLon, 90, 51, 10, t => 1000 * t, _ => 100_000);

        var result = TrackThinner.Thin(source, Tol);

        Assert.True(result.Count > 25, $"Only {result.Count} of {source.Length} points kept.");
        AssertOrderedSubset(source, result);
        AssertDroppedWithinTolerance(source, result);
    }

    [Fact]
    public void Thin_AcceleratingStraightLine_KeepsPoints_BecauseOfTimeSync()
    {
        // Geometrically a straight line, but the speed grows: chord-only DP would keep 2 points.
        var source = AlongGeodesic(PadLat, PadLon, 95, 300, 1, t => 0.5 * 2 * t * t, _ => 0);

        var result = TrackThinner.Thin(source, Tol);

        Assert.True(result.Count > 10, $"Only {result.Count} points kept.");
        Assert.True(result.Count < source.Length);
        AssertDroppedWithinTolerance(source, result);
    }

    [Fact]
    public void Thin_HoldThenLiftoff_KeepsLastHoldPoint()
    {
        // Identical positions (pad hold), then a vertical climb: the moment the vehicle starts moving must stay.
        var source = Enumerable.Range(0, 40)
            .Select(i => Pt(i, PadLat, PadLon, i < 10 ? 0 : 0.5 * 20 * (i - 9) * (i - 9)))
            .ToArray();

        var result = TrackThinner.Thin(source, Tol);

        Assert.Contains(source[9], result);
        Assert.DoesNotContain(source[5], result);
        AssertDroppedWithinTolerance(source, result);
    }

    [Fact]
    public void Thin_AllPointsIdentical_KeepsEndpointsOnly()
    {
        var source = Enumerable.Range(0, 10).Select(i => Pt(i, PadLat, PadLon, 0)).ToArray();

        var result = TrackThinner.Thin(source, Tol);

        Assert.Equal([source[0], source[^1]], result);
    }

    [Fact]
    public void Thin_CurvedAscent_EveryDroppedPointWithinTolerance_AndOrderedSubset()
    {
        var source = CurvedAscent();

        var result = TrackThinner.Thin(source, Tol);

        Assert.Same(source[0], result[0]);
        Assert.Same(source[^1], result[^1]);
        Assert.True(result.Count < source.Length, "Smooth ascent should be thinned.");
        AssertOrderedSubset(source, result);
        AssertDroppedWithinTolerance(source, result);
    }

    [Fact]
    public void Thin_KeptPoints_KeepTheirValuesAndTimestamps()
    {
        var source = CurvedAscent(200);
        var snapshot = source.Select(p => (p.OffsetSeconds, p.Latitude, p.Longitude, p.AltitudeMeters)).ToArray();

        var result = TrackThinner.Thin(source, Tol);

        foreach (var p in result)
        {
            var i = Array.IndexOf(source, p);
            Assert.Equal(snapshot[i], (p.OffsetSeconds, p.Latitude, p.Longitude, p.AltitudeMeters));
        }

        // The input list itself is untouched.
        Assert.Equal(snapshot, source.Select(p => (p.OffsetSeconds, p.Latitude, p.Longitude, p.AltitudeMeters)));
    }

    [Fact]
    public void Thin_IsIdempotent()
    {
        var once = TrackThinner.Thin(CurvedAscent(), Tol);

        var twice = TrackThinner.Thin(once, Tol);

        Assert.Equal(once.Count, twice.Count);
        for (var i = 0; i < once.Count; i++)
        {
            Assert.Same(once[i], twice[i]);
        }
    }

    [Fact]
    public void Thin_ZeroTolerance_DropsOnlyPointsExactlyOnTheLine()
    {
        var source = CurvedAscent(100);

        var result = TrackThinner.Thin(source, 0);

        // Pad hold points are exactly on the line; moving ones are not (float noise aside, nothing beyond 0 m).
        foreach (var p in source.Except(result))
        {
            var k = result.Count(r => r.OffsetSeconds < p.OffsetSeconds);
            Assert.Equal(0, TrackThinner.TimeSyncedError(result[k - 1], result[k], p));
        }
    }

    [Fact]
    public void Thin_AntimeridianCrossing_ConstantSpeed_CollapsesToEndpoints()
    {
        var source = AlongGeodesic(-20, 179.995, 90, 200, 1, t => 5 * t, t => 100_000 + (0.5 * t));
        Assert.Contains(source, p => p.Longitude > 179);
        Assert.Contains(source, p => p.Longitude < -179);

        var result = TrackThinner.Thin(source, Tol);

        Assert.Equal([source[0], source[^1]], result);
    }

    [Fact]
    public void Thin_AntimeridianCrossing_CurvedPath_WithinTolerance()
    {
        var points = new List<TrajectoryPoint>();
        double lat = -20, lon = 179.8, alt = 120_000;
        for (var i = 0; i < 300; i++)
        {
            points.Add(Pt(i, lat, lon, alt));
            (lat, lon) = GeodesicCalculator.Direct(lat, lon, 80 + (i * 0.01), 1000);
            alt -= 5 + (i * 0.05);
        }

        var source = points.ToArray();
        var result = TrackThinner.Thin(source, Tol);

        Assert.True(result.Count < source.Length);
        AssertOrderedSubset(source, result);
        AssertDroppedWithinTolerance(source, result);
    }

    [Fact]
    public void Thin_OverTheNorthPole_ConstantSpeed_CollapsesToEndpoints()
    {
        // Due north from 89.996 deg (~450 m from the pole) passes it within 1 km (longitude jumps by 180 deg there).
        var source = AlongGeodesic(89.996, 10, 0, 200, 1, t => 5 * t, _ => 200_000);
        Assert.Contains(source, p => Math.Abs(p.Longitude - 10) > 90);

        var result = TrackThinner.Thin(source, Tol);

        Assert.Equal([source[0], source[^1]], result);
    }

    [Fact]
    public void Thin_NotStrictlyIncreasingOffsets_Throws()
    {
        var source = new[] { Pt(0, PadLat, PadLon, 0), Pt(1, PadLat, PadLon, 0), Pt(1, PadLat, PadLon, 0), Pt(2, PadLat, PadLon, 0) };

        Assert.Throws<ArgumentException>(() => TrackThinner.Thin(source, Tol));
    }

    [Fact]
    public void Thin_NullList_Throws() =>
        Assert.Throws<ArgumentNullException>(() => TrackThinner.Thin(null!, Tol));

    [Fact]
    public void Thin_NullElement_Throws() =>
        Assert.Throws<ArgumentException>(() => TrackThinner.Thin([Pt(0, PadLat, PadLon, 0), null!], Tol));

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Thin_InvalidTolerance_Throws(double tolerance) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackThinner.Thin(CurvedAscent(10), tolerance));
}
