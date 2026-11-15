using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Tests.Models;

public class TelemetrySampleTests
{
    private static readonly DateTimeOffset Ts = new(2026, 12, 1, 0, 0, 5, TimeSpan.Zero);

    private static TelemetrySample Make(
        double? lat = 25.99,
        double? lon = -97.15,
        double? alt = 100,
        double? vel = null,
        double? heading = null,
        double? pitch = null,
        double? roll = null,
        DateTimeOffset? timestamp = null,
        DateTimeOffset? received = null,
        string source = "Internal simulator") =>
        new(VehicleType.Starship, timestamp ?? Ts, received ?? Ts, lat, lon, alt, vel, heading, pitch, roll, DataQuality.Simulated, source);

    [Fact]
    public void Valid_SetsFieldsAndFlags()
    {
        var s = Make(vel: 12.5, heading: 90, pitch: 10, roll: -5);
        Assert.True(s.HasPosition);
        Assert.True(s.HasOrientation);
        Assert.True(s.IsEstimated);
        Assert.Equal(12.5, s.VelocityMetersPerSecond);
    }

    [Fact]
    public void NoPosition_IsAllowed()
    {
        var s = Make(lat: null, lon: null, alt: null);
        Assert.False(s.HasPosition);
        Assert.False(s.HasOrientation);
    }

    [Theory]
    [InlineData(25.0, null)]
    [InlineData(null, -97.0)]
    public void LatitudeWithoutLongitude_Throws(double? lat, double? lon) =>
        Assert.Throws<ArgumentException>(() => Make(lat, lon));

    [Theory]
    [InlineData(90.0001, 0.0)]
    [InlineData(-90.0001, 0.0)]
    [InlineData(0.0, 180.0001)]
    [InlineData(0.0, -180.0001)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.PositiveInfinity)]
    public void OutOfRangePosition_Throws(double lat, double lon) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(lat, lon));

    [Fact]
    public void Longitude180_NormalisedToMinus180() => Assert.Equal(-180, Make(0, 180).Longitude);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteAltitudeOrVelocity_Throws(double v)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(alt: v));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(vel: v));
    }

    [Theory]
    [InlineData(-90.0001)]
    [InlineData(90.0001)]
    [InlineData(double.NaN)]
    public void BadPitch_Throws(double pitch) => Assert.Throws<ArgumentOutOfRangeException>(() => Make(pitch: pitch));

    [Theory]
    [InlineData(-180.0001)]
    [InlineData(180.0001)]
    [InlineData(double.NaN)]
    public void BadRoll_Throws(double roll) => Assert.Throws<ArgumentOutOfRangeException>(() => Make(roll: roll));

    [Theory]
    [InlineData(360.0, 0.0)]
    [InlineData(-90.0, 270.0)]
    [InlineData(450.0, 90.0)]
    public void Heading_IsNormalised(double input, double expected) => Assert.Equal(expected, Make(heading: input).HeadingDegrees);

    [Fact]
    public void NaNHeading_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => Make(heading: double.NaN));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankSource_Throws(string source) => Assert.Throws<ArgumentException>(() => Make(source: source));

    [Fact]
    public void NonUtcTimestamps_Throw()
    {
        var local = new DateTimeOffset(2026, 12, 1, 2, 0, 5, TimeSpan.FromHours(2));
        Assert.Throws<ArgumentException>(() => Make(timestamp: local));
        Assert.Throws<ArgumentException>(() => Make(received: local));
    }

    [Theory]
    [InlineData(DataQuality.Measured, false)]
    [InlineData(DataQuality.Published, false)]
    [InlineData(DataQuality.Simulated, true)]
    [InlineData(DataQuality.Estimated, true)]
    public void IsEstimated_FollowsQuality(DataQuality quality, bool expected)
    {
        var s = new TelemetrySample(VehicleType.SuperHeavy, Ts, Ts, 1, 1, 1, null, null, null, null, quality, "src");
        Assert.Equal(expected, s.IsEstimated);
    }
}
