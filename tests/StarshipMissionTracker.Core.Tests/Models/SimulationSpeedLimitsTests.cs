using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Tests.Models;

public class SimulationSpeedLimitsTests
{
    [Fact]
    public void Constants_MatchPlan()
    {
        Assert.Equal(0.1, SimulationSpeedLimits.Min);
        Assert.Equal(100, SimulationSpeedLimits.Max);
        Assert.Equal(10, SimulationSpeedLimits.Default);
        Assert.True(SimulationSpeedLimits.IsValid(SimulationSpeedLimits.Default));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(100.0)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(10.0)]
    public void InRangeIncludingBounds_IsValid(double speed) => Assert.True(SimulationSpeedLimits.IsValid(speed));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(0.0999)]
    [InlineData(100.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void OutOfRangeOrNonFinite_IsInvalid(double speed) => Assert.False(SimulationSpeedLimits.IsValid(speed));

    [Fact]
    public void StartRequest_ValidatesSpeedAndCountdown()
    {
        var ok = new SimulationStartRequest("demo-ift", 10, 10);
        Assert.Equal(10, ok.Speed);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationStartRequest("demo-ift", 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationStartRequest("demo-ift", double.NaN, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationStartRequest("demo-ift", 10, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationStartRequest("demo-ift", 10, double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new SimulationStartRequest(" ", 10, 10));
    }
}
