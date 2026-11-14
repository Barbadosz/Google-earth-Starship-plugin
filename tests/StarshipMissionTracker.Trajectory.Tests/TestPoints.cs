using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Trajectory.Tests;

internal static class TestPoints
{
    public const string Source = "test-source";

    public static TrajectoryPoint At(
        double t,
        double lat,
        double lon,
        double alt = 0,
        DataQuality quality = DataQuality.Reconstructed,
        double? velocity = null,
        double? heading = null,
        double? pitch = null,
        double? roll = null) =>
        new(t, lat, lon, alt, velocity, quality, Source, heading, pitch, roll);
}
