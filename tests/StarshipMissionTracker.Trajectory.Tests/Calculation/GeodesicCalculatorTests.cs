using StarshipMissionTracker.Trajectory.Calculation;

namespace StarshipMissionTracker.Trajectory.Tests.Calculation;

public class GeodesicCalculatorTests
{
    // WGS84: a = 6378137 m, so one degree of longitude on the equator is a * pi / 180 = 111319.490793 m.
    private const double EquatorDegreeMeters = 111_319.490793;

    // Quarter meridian (equator to pole) on WGS84: 10001965.729 m (Karney 2013, "Algorithms for geodesics").
    private const double QuarterMeridianMeters = 10_001_965.729;

    [Fact]
    public void Inverse_JfkToLhr_MatchesGeographicLibDocumentation()
    {
        // GeographicLib documentation example (JFK 40.6N 73.8W -> LHR 51.6N 0.5W):
        // s12 = 5551.759400319 km, azi1 = 51.198882845 deg, azi2 = 107.821776735 deg.
        var result = GeodesicCalculator.Inverse(40.6, -73.8, 51.6, -0.5);

        Assert.Equal(5_551_759.400319, result.DistanceMeters, 1e-3);
        Assert.Equal(51.198882845, result.InitialAzimuthDegrees, 1e-6);
        Assert.Equal(107.821776735, result.FinalAzimuthDegrees, 1e-6);
    }

    [Fact]
    public void Inverse_WellingtonToSalamanca_MatchesGeographicLibDocumentation()
    {
        // GeographicLib Python documentation example: 41.32S 174.81E -> 40.96N 5.50W, s12 = 19959679.267 m.
        var result = GeodesicCalculator.Inverse(-41.32, 174.81, 40.96, -5.50);

        Assert.Equal(19_959_679.267, result.DistanceMeters, 1e-2);
    }

    [Fact]
    public void Inverse_EquatorToPole_IsQuarterMeridianDueNorth()
    {
        var result = GeodesicCalculator.Inverse(0, 10, 90, 10);

        Assert.Equal(QuarterMeridianMeters, result.DistanceMeters, 1e-2);
        Assert.Equal(0, result.InitialAzimuthDegrees, 1e-9);
    }

    [Fact]
    public void Inverse_OneDegreeAlongEquator_IsSemiMajorAxisArc()
    {
        var result = GeodesicCalculator.Inverse(0, 0, 0, 1);

        Assert.Equal(EquatorDegreeMeters, result.DistanceMeters, 1e-3);
        Assert.Equal(90, result.InitialAzimuthDegrees, 1e-9);
    }

    [Fact]
    public void Inverse_AcrossAntimeridian_TakesShortWayEast()
    {
        var result = GeodesicCalculator.Inverse(0, 179.5, 0, -179.5);

        Assert.Equal(EquatorDegreeMeters, result.DistanceMeters, 1e-3);
        Assert.Equal(90, result.InitialAzimuthDegrees, 1e-9);
    }

    [Fact]
    public void Inverse_IdenticalPoints_ZeroDistance()
    {
        var result = GeodesicCalculator.Inverse(25.99, -97.15, 25.99, -97.15);

        Assert.Equal(0, result.DistanceMeters, 1e-9);
    }

    [Fact]
    public void Direct_EastAcrossAntimeridian_NormalisesLongitude()
    {
        var (lat, lon) = GeodesicCalculator.Direct(0, 179.9, 90, EquatorDegreeMeters);

        Assert.Equal(0, lat, 1e-9);
        Assert.Equal(-179.1, lon, 1e-7);
    }

    [Fact]
    public void Direct_InverseOfJfkToLhr_ReturnsLhr()
    {
        var (lat, lon) = GeodesicCalculator.Direct(40.6, -73.8, 51.198882845, 5_551_759.400319);

        Assert.Equal(51.6, lat, 1e-6);
        Assert.Equal(-0.5, lon, 1e-6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(180, -180)]
    [InlineData(-180, -180)]
    [InlineData(179.5, 179.5)]
    [InlineData(180.5, -179.5)]
    [InlineData(-180.5, 179.5)]
    [InlineData(540, -180)]
    [InlineData(-720.25, -0.25)]
    public void NormalizeLongitude_AnyValue_InHalfOpenRange(double input, double expected)
    {
        Assert.Equal(expected, GeodesicCalculator.NormalizeLongitude(input), 1e-9);
    }

    [Fact]
    public void NormalizeLongitude_NotFinite_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeodesicCalculator.NormalizeLongitude(double.NaN));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(360, 0)]
    [InlineData(-90, 270)]
    [InlineData(725, 5)]
    public void NormalizeAzimuth_AnyValue_InZeroTo360(double input, double expected)
    {
        Assert.Equal(expected, GeodesicCalculator.NormalizeAzimuth(input), 1e-9);
    }

    [Fact]
    public void Segment_PositionAtHalf_AcrossAntimeridian_IsOnAntimeridianNormalised()
    {
        var segment = new GeodesicSegment(0, 179.5, 0, -179.5);

        var (lat, lon, azimuth) = segment.PositionAt(0.5);

        Assert.Equal(0, lat, 1e-9);
        Assert.Equal(-180, lon, 1e-9);
        Assert.Equal(90, azimuth, 1e-9);
    }

    [Fact]
    public void Segment_PositionAtOutOfRange_Throws()
    {
        var segment = new GeodesicSegment(0, 0, 0, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => segment.PositionAt(1.5));
    }
}
