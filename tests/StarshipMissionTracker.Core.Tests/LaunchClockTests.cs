using System.Globalization;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Tests;

public class LaunchClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 12, 1, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ToUtc_PositiveOffset_AddsSeconds()
    {
        var clock = new LaunchClock(T0);
        Assert.Equal(new DateTimeOffset(2026, 12, 2, 0, 0, 0, TimeSpan.Zero), clock.ToUtc(1800));
    }

    [Fact]
    public void ToUtc_NegativeOffset_SubtractsSeconds()
    {
        var clock = new LaunchClock(T0);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 23, 29, 0, TimeSpan.Zero), clock.ToUtc(-60));
    }

    [Fact]
    public void ToUtc_NonFinite_Throws()
    {
        var clock = new LaunchClock(T0);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.ToUtc(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.ToUtc(double.PositiveInfinity));
    }

    [Fact]
    public void Constructor_NonUtcOffset_Throws()
    {
        var local = new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.FromHours(2));
        Assert.Throws<ArgumentException>(() => new LaunchClock(local));
    }

    [Fact]
    public void Format_Seconds_UsesZSuffix()
    {
        var clock = new LaunchClock(T0);
        Assert.Equal("2026-12-01T23:30:00Z", clock.Format(0));
        Assert.Equal("2026-12-01T23:30:05Z", clock.Format(5.4));
    }

    [Fact]
    public void Format_Milliseconds_IncludesFraction()
    {
        var clock = new LaunchClock(T0);
        Assert.Equal("2026-12-01T23:30:01.500Z", clock.Format(1.5, includeMilliseconds: true));
    }

    [Theory]
    [InlineData("2026-12-01T23:30:00Z")]
    [InlineData("2026-12-01T23:30:00.250Z")]
    public void ParseUtc_ValidZ_ReturnsUtc(string text)
    {
        Assert.Equal(TimeSpan.Zero, LaunchClock.ParseUtc(text).Offset);
    }

    [Fact]
    public void Format_NearSecondBoundary_TruncatesAfterTickRounding()
    {
        var clock = new LaunchClock(T0);
        Assert.Equal("2026-12-01T23:30:00Z", clock.Format(0.9999));
        Assert.Equal("2026-12-01T23:30:00.999Z", clock.Format(0.9999, includeMilliseconds: true));
        // Rounds up to a whole tick first: 0.99999999 s -> 1 s.
        Assert.Equal("2026-12-01T23:30:01Z", clock.Format(0.99999999));
        Assert.Equal("2026-12-01T23:29:59Z", clock.Format(-0.0001));
        Assert.Equal("2026-12-01T23:29:59.999Z", clock.Format(-0.0001, includeMilliseconds: true));
        Assert.Equal("2026-12-02T00:00:00Z", clock.Format(1800));
    }

    [Fact]
    public void FormatExact_GivesSevenDigits_AndParsesBack()
    {
        var clock = new LaunchClock(T0);
        var text = clock.FormatExact(1.0000001);
        Assert.Equal("2026-12-01T23:30:01.0000001Z", text);
        Assert.Equal(T0.AddTicks(10_000_001), LaunchClock.ParseUtc(text));
    }

    [Fact]
    public void FormatExact_UsesInvariantCulture_UnderHungarian()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("hu-HU");
            var clock = new LaunchClock(T0);
            Assert.Equal("2026-12-01T23:30:02.5000000Z", clock.FormatExact(2.5));
            Assert.Equal("2026-12-01T23:30:02.500Z", clock.Format(2.5, includeMilliseconds: true));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    [Fact]
    public void Format_FractionalT0_GivesMilliseconds()
    {
        var clock = new LaunchClock(new DateTimeOffset(2026, 12, 1, 14, 0, 0, 500, TimeSpan.Zero));
        Assert.Equal("2026-12-01T14:00:00.500Z", clock.Format(0));
        Assert.Equal("2026-12-01T14:00:05.500Z", clock.Format(5));
    }

    [Fact]
    public void Format_WholeSecondT0_IsUnchanged()
    {
        var clock = new LaunchClock(T0);
        Assert.Equal("2026-12-01T23:30:05Z", clock.Format(5));
    }

    [Fact]
    public void ToUtc_HugeOffset_ThrowsClearError()
    {
        var clock = new LaunchClock(T0);
        var ex = Assert.Throws<MissionDataException>(() => clock.ToUtc(1e300));
        Assert.Contains("too large", ex.Message);
        var ex2 = Assert.Throws<MissionDataException>(() => clock.ToUtc(-1e11));
        Assert.Contains("date range", ex2.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.ToUtc(double.NegativeInfinity));
    }

    [Theory]
    [InlineData("2026-12-01T23:30:00.1234567Z")]
    [InlineData("2026-12-01T23:30:00.5Z")]
    public void ParseUtc_FractionVariants_Accepted(string text)
    {
        Assert.True(LaunchClock.TryParseUtc(text, out var v));
        Assert.Equal(TimeSpan.Zero, v.Offset);
    }

    [Fact]
    public void ParseUtc_Value_IsExact()
    {
        Assert.Equal(T0, LaunchClock.ParseUtc("2026-12-01T23:30:00Z"));
        Assert.Equal(T0, LaunchClock.FromText("2026-12-01T23:30:00Z").LaunchTimeUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Dec 1 2026 23:30Z")]
    [InlineData("2026-12-01Z")]
    [InlineData(" 2026-12-01T00:00:00Z")]
    [InlineData("2026-12-01T00:00:00Z ")]
    [InlineData("2026-12-01T00:00:00z")]
    [InlineData("2026-12-01 00:00:00Z")]
    [InlineData("2026-12-01T00:00Z")]
    [InlineData("2026-12-01T00:00:00.Z")]
    [InlineData("2026-12-01T00:00:00.12345678Z")]
    [InlineData("2026-12-01T24:00:00Z")]
    [InlineData("2026-13-01T00:00:00Z")]
    [InlineData("12/01/2026 23:30:00Z")]
    [InlineData("2026-12-01T23:30:00")]
    [InlineData("2026-12-01T23:30:00+02:00")]
    [InlineData("2026-12-01T23:30:00+00:00")]
    [InlineData("not a date")]
    public void ParseUtc_NotStrictUtc_Throws(string text)
    {
        Assert.Throws<FormatException>(() => LaunchClock.ParseUtc(text));
        Assert.False(LaunchClock.TryParseUtc(text, out _));
    }
}
