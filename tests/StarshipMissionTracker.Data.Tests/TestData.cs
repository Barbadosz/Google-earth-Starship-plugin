using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Data.Tests;

internal static class TestData
{
    public const string Code = "test-mission";

    /// <summary>T0 with a sub-second part to prove the fraction survives.</summary>
    public static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.Zero).AddTicks(1234567);

    public static Mission Mission(
        string code = Code,
        DateTimeOffset? t0 = null,
        DataQuality pathQuality = DataQuality.Simulated,
        bool starshipOnly = false)
    {
        var events = new List<MissionEvent>
        {
            new(0, EventType.Liftoff, "Liftoff", "Test liftoff", 25.9972, -97.1566, 5, null, pathQuality, "test source"),
            new(60, EventType.MaxQ, "Max Q", null, null, null, null, VehicleType.Starship, pathQuality, "test source"),
        };
        var vehicles = new List<Vehicle> { MakeVehicle("Starship", VehicleType.Starship, "files/starship.dae", pathQuality) };
        if (!starshipOnly)
        {
            vehicles.Add(MakeVehicle("Super Heavy", VehicleType.SuperHeavy, null, pathQuality));
        }

        var time = t0 ?? T0;
        return new Mission(new MissionConfig(code, "Test mission", time, "A test mission", events), new LaunchClock(time), vehicles);
    }

    private static Vehicle MakeVehicle(string name, VehicleType type, string? model, DataQuality quality)
    {
        var points = new List<TrajectoryPoint>
        {
            new(0, 25.9972, -97.1566, 5, 0, quality, "test source", 90, 90, 0),
            new(10.5, 25.9973, -97.15, 800, 120, quality, "test source", 90, 80, 0),
            new(20, 25.9975, -97.13, 2500, 240.25, quality, "test source"),
        };
        return new Vehicle(name, type, model, [new FlightPath(TrajectoryType.Planned, quality, "test source", "CC0", points)]);
    }

    public static TelemetrySample Sample(
        VehicleType vehicle,
        DateTimeOffset timestamp,
        double? lat = 26.0,
        double? lon = -97.0,
        double? alt = 1000,
        DataQuality quality = DataQuality.Simulated,
        string source = DataSourceNames.InternalSimulator) =>
        new(vehicle, timestamp, timestamp.AddMilliseconds(5), lat, lon, alt, 100, 90, 45, 0, quality, source);
}
