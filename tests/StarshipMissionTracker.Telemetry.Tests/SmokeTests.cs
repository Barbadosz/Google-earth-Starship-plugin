namespace StarshipMissionTracker.Telemetry.Tests;

public class SmokeTests
{
    [Fact]
    public void TelemetryAssembly_CanBeReferenced()
    {
        // Scaffold check (M2 package 0); real tests arrive with package C.
        Assert.True(typeof(StarshipMissionTracker.Core.Enums.VehicleType).IsEnum);
    }
}
