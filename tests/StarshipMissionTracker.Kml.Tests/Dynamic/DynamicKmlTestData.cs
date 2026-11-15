using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Kml.Tests.Dynamic;

/// <summary>Small synthetic missions and samples for the server-mode KML tests (no files, no network).</summary>
internal static class DynamicKmlTestData
{
    public static readonly DateTimeOffset T0 = new(2026, 12, 1, 14, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Now = T0.AddSeconds(100);

    public const double PadLat = 25.9972;
    public const double PadLon = -97.1566;

    public static readonly KmlAssetUrls Assets = new("http://localhost:5000/assets/models", "http://localhost:5000/assets/icons/");

    public static readonly KmlEndpointUrls Urls = new(
        "http://localhost:5000/kml/planned?mission=test",
        "http://localhost:5000/kml/history?mission=test",
        "http://localhost:5000/kml/live?mission=test");

    public static NetworkLinkSettings Settings { get; } =
        new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

    public static MissionInfo Info(bool simulated = true, bool launchSite = true) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "test", "Test mission", "Illustrative test data.",
            MissionStatus.InFlight, T0, T0, simulated, T0, launchSite ? (PadLat, PadLon) : null);

    public static TrajectoryPoint Pt(double t, double lat, double lon, double alt, DataQuality quality = DataQuality.Simulated) =>
        new(t, lat, lon, alt, null, quality, "test source", 90, 45, 0);

    public static FlightPath Path(TrajectoryType type, int points, DataQuality quality = DataQuality.Simulated)
    {
        var list = new List<TrajectoryPoint>();
        for (var i = 0; i < points; i++)
        {
            list.Add(Pt(i * 0.5, PadLat + i * 0.0001, PadLon + i * 0.001, 5 + i * 10.25, quality));
        }

        return new FlightPath(type, quality, "test source", "MIT", list);
    }

    public static Mission Mission(IReadOnlyList<Vehicle> vehicles)
    {
        var events = new List<MissionEvent>
        {
            new(0, EventType.Liftoff, "Liftoff", "", PadLat, PadLon, 5, null, DataQuality.Simulated, "test source"),
            new(160.5, EventType.StageSeparation, "Stage Separation", "Hot staging", 26.0, -96.2, 68000, null, DataQuality.Simulated, "test source"),
            new(520, EventType.Coast, "Coast", "", null, null, null, VehicleType.Starship, DataQuality.Simulated, "test source"),
        };
        var config = new MissionConfig("test", "Test mission", T0, "Illustrative test data.", events);
        return new Mission(config, new LaunchClock(T0), vehicles);
    }

    public static Vehicle Starship(params FlightPath[] paths) =>
        new("Starship", VehicleType.Starship, "models/starship.dae", paths);

    public static Vehicle SuperHeavy(params FlightPath[] paths) =>
        new("Super Heavy", VehicleType.SuperHeavy, "models/superheavy.dae", paths);

    public static Mission PlannedMission(int points = 5) =>
        Mission([Starship(Path(TrajectoryType.Planned, points)), SuperHeavy(Path(TrajectoryType.Planned, points))]);

    public static TelemetrySample Sample(
        VehicleType vehicle = VehicleType.Starship,
        double lat = PadLat,
        double lon = PadLon,
        double? alt = 1234.56,
        double? heading = 90,
        double? pitch = 80,
        double? roll = 0,
        double? velocity = 250.5) =>
        new(vehicle, Now, Now, lat, lon, alt, velocity, heading, pitch, roll, DataQuality.Simulated, "Internal simulator");

    public static LiveVehicleState Live(Vehicle vehicle, TelemetrySample? sample, bool stale = false, double? ageSeconds = 1) =>
        new(vehicle, sample, ageSeconds is { } a ? TimeSpan.FromSeconds(a) : null, stale);
}
