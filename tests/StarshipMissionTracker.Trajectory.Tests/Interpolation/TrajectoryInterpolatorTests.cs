using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Calculation;
using StarshipMissionTracker.Trajectory.Interpolation;
using static StarshipMissionTracker.Trajectory.Tests.TestPoints;

namespace StarshipMissionTracker.Trajectory.Tests.Interpolation;

public class TrajectoryInterpolatorTests
{
    private static readonly TrajectoryPoint[] Path =
    [
        At(-10, 0, 0, 0),
        At(0, 0, 0, 0, velocity: 0),
        At(100, 0, 1, 10_000, velocity: 2000),
        At(200, 10, 1, 20_000, velocity: 4000),
    ];

    [Fact]
    public void PointAt_ExactOffset_ReturnsOriginalPoint()
    {
        Assert.Same(Path[2], TrajectoryInterpolator.PointAt(Path, 100));
        Assert.Same(Path[0], TrajectoryInterpolator.PointAt(Path, -10));
        Assert.Same(Path[3], TrajectoryInterpolator.PointAt(Path, 200));
    }

    [Fact]
    public void PointAt_BetweenPoints_GeodesicPositionLinearAltitudeEstimated()
    {
        var p = TrajectoryInterpolator.PointAt(Path, 25)!;

        Assert.Equal(25, p.OffsetSeconds);
        Assert.Equal(0, p.Latitude, 1e-9);
        Assert.Equal(0.25, p.Longitude, 1e-9);
        Assert.Equal(2500, p.AltitudeMeters, 1e-9);
        Assert.Equal(500, p.VelocityMetersPerSecond!.Value, 1e-9);
        Assert.Equal(DataQuality.Estimated, p.Quality);
    }

    [Fact]
    public void PointAt_AlongMeridian_HalfDistanceNotHalfLatitudeAssumption()
    {
        // Along a meridian the geodesic midpoint (by distance) is checked against the geodesic distances.
        var p = TrajectoryInterpolator.PointAt(Path, 150)!;

        var first = GeodesicCalculator.Inverse(0, 1, p.Latitude, p.Longitude).DistanceMeters;
        var second = GeodesicCalculator.Inverse(p.Latitude, p.Longitude, 10, 1).DistanceMeters;
        Assert.Equal(first, second, 1e-3);
        Assert.Equal(1, p.Longitude, 1e-9);
    }

    [Fact]
    public void PointAt_MissingVelocityOnOneSide_VelocityNull()
    {
        var p = TrajectoryInterpolator.PointAt(Path, -5)!;

        Assert.Null(p.VelocityMetersPerSecond);
    }

    [Fact]
    public void PointAt_AcrossAntimeridian_MidpointNormalised()
    {
        TrajectoryPoint[] path = [At(0, 0, 179.5), At(10, 0, -179.5)];

        var p = TrajectoryInterpolator.PointAt(path, 5)!;

        Assert.InRange(p.Longitude, -180, 179.999999999);
        Assert.Equal(180, Math.Abs(p.Longitude), 1e-9);
        Assert.Equal(-179.75, TrajectoryInterpolator.PointAt(path, 7.5)!.Longitude, 1e-9);
    }

    [Fact]
    public void PointAt_OutsideRangeOrEmpty_NullNoExtrapolation()
    {
        Assert.Null(TrajectoryInterpolator.PointAt(Path, -10.001));
        Assert.Null(TrajectoryInterpolator.PointAt(Path, 200.001));
        Assert.Null(TrajectoryInterpolator.PointAt([], 0));
    }

    [Fact]
    public void PointAt_InvalidInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => TrajectoryInterpolator.PointAt([At(1, 0, 0), At(1, 0, 1)], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrajectoryInterpolator.PointAt(Path, double.NaN));
        Assert.Throws<ArgumentNullException>(() => TrajectoryInterpolator.PointAt(null!, 0));
    }
}
