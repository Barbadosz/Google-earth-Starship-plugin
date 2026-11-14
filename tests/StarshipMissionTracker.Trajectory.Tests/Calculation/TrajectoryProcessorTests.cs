using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Calculation;
using static StarshipMissionTracker.Trajectory.Tests.TestPoints;

namespace StarshipMissionTracker.Trajectory.Tests.Calculation;

public class TrajectoryProcessorTests
{
    private const double EquatorDegreeMeters = 111_319.490793;

    private readonly TrajectoryProcessor _processor = new();

    [Fact]
    public void Densify_LongSegment_NeighboursWithinBothLimits()
    {
        TrajectoryPoint[] input = [At(0, 25.99, -97.15, 0), At(300, 24.5, -88.0, 150_000), At(600, 23.0, -80.0, 160_000)];
        var options = new TrajectoryDensificationOptions(10_000, 10);

        var result = _processor.Densify(input, options);

        for (var i = 1; i < result.Count; i++)
        {
            var distance = GeodesicCalculator.Inverse(
                result[i - 1].Latitude, result[i - 1].Longitude, result[i].Latitude, result[i].Longitude).DistanceMeters;
            Assert.True(distance <= options.MaxSpacingMeters + 1e-6, $"gap {i}: {distance} m");
            Assert.True(result[i].OffsetSeconds - result[i - 1].OffsetSeconds <= options.MaxSpacingSeconds + 1e-9);
        }
    }

    [Fact]
    public void Densify_Always_OriginalPointsPreservedAndOffsetsStrictlyIncreasing()
    {
        TrajectoryPoint[] input = [At(0, 25.99, -97.15, 0, velocity: 0), At(95, 25.5, -95.0, 60_000, velocity: 1500)];

        var result = _processor.Densify(input, TrajectoryDensificationOptions.Default);

        Assert.Same(input[0], result[0]);
        Assert.Same(input[1], result[^1]);
        Assert.Equal(DataQuality.Reconstructed, input[0].Quality);
        for (var i = 1; i < result.Count; i++)
        {
            Assert.True(result[i].OffsetSeconds > result[i - 1].OffsetSeconds);
        }

        // Builds a valid FlightPath only when its quality is Estimated or worse (Core rule).
        var path = new FlightPath(TrajectoryType.Planned, DataQuality.Estimated, Source, null, result);
        Assert.Equal(result.Count, path.Points.Count);
    }

    [Fact]
    public void Densify_InsertedPoints_AreEstimatedWithLinearAltitudeAndVelocity()
    {
        TrajectoryPoint[] input = [At(0, 0, 0, 1000, velocity: 100), At(40, 0, 0.2, 5000, velocity: 300)];

        var result = _processor.Densify(input, new TrajectoryDensificationOptions(1_000_000, 10));

        Assert.Equal(5, result.Count);
        for (var i = 1; i < 4; i++)
        {
            var p = result[i];
            Assert.Equal(DataQuality.Estimated, p.Quality);
            Assert.True(p.IsEstimated);
            Assert.Equal(i * 10, p.OffsetSeconds, 1e-9);
            Assert.Equal(1000 + (i * 1000), p.AltitudeMeters, 1e-9);
            Assert.Equal(100 + (i * 50), p.VelocityMetersPerSecond!.Value, 1e-9);
            Assert.Equal(0, p.Latitude, 1e-9);
            Assert.Equal(i * 0.05, p.Longitude, 1e-9);
            Assert.Equal(Source, p.SourceName);
        }
    }

    [Fact]
    public void Densify_DistanceLimitOnly_SplitsByDistance()
    {
        // 1 degree on the equator (~111.3 km) in 5 s: time limit not exceeded, distance limit -> 12 pieces.
        TrajectoryPoint[] input = [At(0, 0, 0), At(5, 0, 1)];

        var result = _processor.Densify(input, new TrajectoryDensificationOptions(10_000, 10));

        Assert.Equal(13, result.Count);
        Assert.Equal(EquatorDegreeMeters / 12, GeodesicCalculator.Inverse(0, 0, 0, result[1].Longitude).DistanceMeters, 1e-3);
    }

    [Fact]
    public void Densify_SegmentExactlyAtLimits_NotSplit()
    {
        TrajectoryPoint[] input = [At(0, 0, 0), At(10, 0, 0.05)];

        var result = _processor.Densify(input, new TrajectoryDensificationOptions(10_000, 10));

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Densify_AcrossAntimeridian_StaysNearDatelineAndNormalised()
    {
        TrajectoryPoint[] input = [At(0, -10, 179.5, 120_000), At(30, -10.2, -179.6, 120_000)];

        var result = _processor.Densify(input, new TrajectoryDensificationOptions(10_000, 10));

        Assert.True(result.Count > 2);
        foreach (var p in result)
        {
            Assert.InRange(p.Longitude, -180, 179.999999999);
            Assert.True(Math.Abs(p.Longitude) >= 179.4, $"point went the long way round: lon {p.Longitude}");
        }

        for (var i = 1; i < result.Count; i++)
        {
            var distance = GeodesicCalculator.Inverse(
                result[i - 1].Latitude, result[i - 1].Longitude, result[i].Latitude, result[i].Longitude).DistanceMeters;
            Assert.True(distance <= 10_000 + 1e-6);
        }
    }

    [Fact]
    public void Densify_VerticalOnlySegment_SplitByTimeSamePosition()
    {
        TrajectoryPoint[] input = [At(0, 25.99, -97.15, 0), At(60, 25.99, -97.15, 12_000)];

        var result = _processor.Densify(input, TrajectoryDensificationOptions.Default);

        Assert.Equal(7, result.Count);
        for (var i = 0; i < result.Count; i++)
        {
            Assert.Equal(25.99, result[i].Latitude, 1e-9);
            Assert.Equal(-97.15, result[i].Longitude, 1e-9);
            Assert.Equal(i * 2000, result[i].AltitudeMeters, 1e-6);
        }
    }

    [Fact]
    public void Densify_ZeroLengthShortSegment_NothingInserted()
    {
        TrajectoryPoint[] input = [At(0, 10, 20, 500), At(5, 10, 20, 500)];

        var result = _processor.Densify(input, TrajectoryDensificationOptions.Default);

        Assert.Equal(input, result);
    }

    [Fact]
    public void Densify_ZeroLengthLongPause_InsertsByTime()
    {
        TrajectoryPoint[] input = [At(0, 10, 20, 500), At(25, 10, 20, 500)];

        var result = _processor.Densify(input, TrajectoryDensificationOptions.Default);

        Assert.Equal([0, 25.0 / 3, 50.0 / 3, 25], result.Select(p => p.OffsetSeconds), new ToleranceComparer(1e-9));
        Assert.All(result, p => Assert.Equal(20, p.Longitude, 1e-9));
    }

    [Fact]
    public void Densify_SimulatedSource_InsertedPointsNotUpgraded()
    {
        TrajectoryPoint[] input = [At(0, 0, 0, quality: DataQuality.Simulated), At(20, 0, 0.01, quality: DataQuality.Simulated)];

        var result = _processor.Densify(input, TrajectoryDensificationOptions.Default);

        Assert.Equal(DataQuality.Simulated, result[1].Quality);
    }

    [Fact]
    public void Densify_OrientationOnBothEnds_InterpolatedAlongShorterArc()
    {
        TrajectoryPoint[] input = [At(0, 0, 0, heading: 350, pitch: 80, roll: 0), At(20, 0, 0.01, heading: 10, pitch: 60, roll: 0)];

        var result = _processor.Densify(input, TrajectoryDensificationOptions.Default);

        Assert.Equal(0, result[1].HeadingDegrees!.Value, 1e-9);
        Assert.Equal(70, result[1].PitchDegrees!.Value, 1e-9);
    }

    [Fact]
    public void Densify_EmptyOrSinglePoint_ReturnedAsCopy()
    {
        Assert.Empty(_processor.Densify([], TrajectoryDensificationOptions.Default));

        TrajectoryPoint[] single = [At(0, 1, 2)];
        var result = _processor.Densify(single, TrajectoryDensificationOptions.Default);
        Assert.Same(single[0], Assert.Single(result));
        Assert.NotSame(single, result);
    }

    [Fact]
    public void Densify_UnsortedOrDuplicateOffsets_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _processor.Densify([At(10, 0, 0), At(5, 0, 1)], TrajectoryDensificationOptions.Default));
        Assert.Throws<ArgumentException>(() =>
            _processor.Densify([At(5, 0, 0), At(5, 0, 1)], TrajectoryDensificationOptions.Default));
    }

    [Fact]
    public void Densify_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => _processor.Densify(null!, TrajectoryDensificationOptions.Default));
        Assert.Throws<ArgumentNullException>(() => _processor.Densify([At(0, 0, 0)], null!));
        Assert.Throws<ArgumentException>(() => _processor.Densify([At(0, 0, 0), null!], TrajectoryDensificationOptions.Default));
    }

    [Fact]
    public void Densify_AbsurdlySmallLimits_ThrowsInsteadOfExhaustingMemory()
    {
        TrajectoryPoint[] input = [At(0, 0, 0), At(1000, 0, 90)];

        Assert.Throws<ArgumentException>(() => _processor.Densify(input, new TrajectoryDensificationOptions(0.001, 10)));
    }

    [Fact]
    public void Densify_OutputExactlyAtCap_Allowed()
    {
        // Vertical-only segment split by time: 999,999 pieces -> 1 + 999,999 = exactly MaxOutputPoints points.
        TrajectoryPoint[] input = [At(0, 0, 0, 0), At(999_999, 0, 0, 1000)];

        var result = _processor.Densify(input, new TrajectoryDensificationOptions(10_000, 1));

        Assert.Equal(TrajectoryProcessor.MaxOutputPoints, result.Count);
    }

    [Fact]
    public void Densify_OutputOnePastCap_Throws()
    {
        TrajectoryPoint[] input = [At(0, 0, 0, 0), At(1_000_000, 0, 0, 1000)];

        Assert.Throws<ArgumentException>(() => _processor.Densify(input, new TrajectoryDensificationOptions(10_000, 1)));
    }

    [Fact]
    public void Densify_TimeDominatedSegment_SplitByTimeEvenWhenShort()
    {
        // About 111 m of ground distance (well under the 10 km limit) but 100 s -> 10 pieces of 10 s.
        TrajectoryPoint[] input = [At(0, 0, 0, 0), At(100, 0, 0.001, 0)];

        var result = _processor.Densify(input, new TrajectoryDensificationOptions(10_000, 10));

        Assert.Equal(11, result.Count);
        for (var i = 1; i < result.Count; i++)
        {
            Assert.Equal(10, result[i].OffsetSeconds - result[i - 1].OffsetSeconds, 1e-9);
        }
    }

    [Fact]
    public void Densify_RollAcrossPlusMinus180_TakesShorterArcAndStaysInRange()
    {
        TrajectoryPoint[] input = [At(0, 0, 0, roll: 170, heading: 0, pitch: 0), At(20, 0, 0.01, roll: -170, heading: 0, pitch: 0)];

        var result = _processor.Densify(input, TrajectoryDensificationOptions.Default);

        Assert.Equal(3, result.Count);
        Assert.Equal(-180, result[1].RollDegrees!.Value, 1e-9);
    }

    private sealed class ToleranceComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;

        public int GetHashCode(double obj) => 0;
    }
}
