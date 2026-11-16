using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Simulation;
using StarshipMissionTracker.Telemetry.Tests.Support;
using StarshipMissionTracker.Telemetry.Validation;

namespace StarshipMissionTracker.Telemetry.Tests;

public class TelemetryValidatorTests
{
    private static readonly DateTimeOffset Now = TestData.Start;

    private static TelemetrySample Sample(
        DateTimeOffset? ts = null,
        DataQuality quality = DataQuality.Simulated,
        string source = PathReplaySampler.SourceName,
        double? lat = 25,
        double? lon = -97,
        double? alt = 100) =>
        new(VehicleType.Starship, ts ?? Now, Now, lat, lon, alt, null, null, null, null, quality, source);

    [Fact]
    public void ValidSample_Passes()
    {
        Assert.True(TelemetryValidator.Validate(Sample(), null, Now).IsValid);
    }

    [Fact]
    public void Timestamps_MustIncreaseStrictlyPerVehicle()
    {
        Assert.False(TelemetryValidator.Validate(Sample(Now), Now, Now).IsValid);
        Assert.False(TelemetryValidator.Validate(Sample(Now), Now.AddSeconds(1), Now).IsValid);
        Assert.True(TelemetryValidator.Validate(Sample(Now.AddSeconds(1)), Now, Now).IsValid);
    }

    [Theory]
    [InlineData(DataQuality.Measured)]
    [InlineData(DataQuality.Published)]
    public void SimulatorSource_CannotBeMeasuredOrPublished(DataQuality quality)
    {
        Assert.False(TelemetryValidator.Validate(Sample(quality: quality), null, Now).IsValid);
    }

    [Fact]
    public void FutureTimestamps_OnlyForSimulator()
    {
        var future = Now.AddHours(1);
        Assert.True(TelemetryValidator.Validate(Sample(future), null, Now).IsValid);
        var real = Sample(future, DataQuality.Measured, "Some adapter");
        Assert.False(TelemetryValidator.Validate(real, null, Now).IsValid);
        Assert.True(TelemetryValidator.Validate(Sample(Now.AddSeconds(1), DataQuality.Measured, "Some adapter"), null, Now).IsValid);
    }

    [Fact]
    public void ModelConstructor_AlreadyRejectsNonFiniteAndPairViolations()
    {
        Assert.Throws<ArgumentException>(() => Sample(lat: 25, lon: null));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample(lat: 91));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample(alt: double.NaN));
    }

    [Fact]
    public void NoPosition_IsValid()
    {
        Assert.True(TelemetryValidator.Validate(Sample(lat: null, lon: null, alt: null), null, Now).IsValid);
    }

    [Theory]
    [InlineData("Internal simulator", true)]
    [InlineData("Internal simulator 2", false)]
    [InlineData("Internal simulator (copy)", false)]
    [InlineData("internal simulator", false)]
    [InlineData("", false)]
    public void IsSimulatorSource_IsExactOrdinalEquality(string source, bool expected)
    {
        Assert.Equal(expected, TelemetryValidator.IsSimulatorSource(source));
    }

    [Fact]
    public void SourceNameWithSimulatorPrefix_IsNotTreatedAsSimulator()
    {
        // A foreign adapter whose name merely starts with the simulator's must not get the future-timestamp exemption.
        var future = Now.AddHours(1);
        var lookalike = Sample(future, DataQuality.Measured, SimulatorSource.Name + " (adapter)");
        Assert.False(TelemetryValidator.Validate(lookalike, null, Now).IsValid);
    }
}
