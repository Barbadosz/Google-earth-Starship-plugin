using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Tests.Support;

/// <summary>Small fixed missions for the endpoint tests (Boca Chica launch site, two vehicles).</summary>
public static class TestData
{
    public const string Code = "demo-ift";
    public const string Source = "Test planned file";
    public const string SimSource = "Internal simulator (replay of Test planned file)";

    public static readonly DateTimeOffset PlannedT0 = new(2026, 12, 1, 23, 30, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Updated = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    public static MissionInfo Info(
        string code = Code,
        DateTimeOffset? planned = null,
        DateTimeOffset? anchor = null,
        DateTimeOffset? updated = null,
        bool isSimulated = true,
        MissionStatus status = MissionStatus.Scheduled) =>
        new(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            code,
            "Demo IFT",
            "Test mission",
            status,
            planned ?? PlannedT0,
            null,
            isSimulated,
            updated ?? Updated,
            (25.997, -97.157),
            anchor);

    /// <summary>Planned mission whose clock is the info's effective T0. Starship ends at T+600, Super Heavy at T+400.</summary>
    public static Mission Mission(MissionInfo info)
    {
        var events = new List<MissionEvent>
        {
            new(0, EventType.Liftoff, "Liftoff", null, 25.997, -97.157, 10, null, DataQuality.Simulated, Source),
            new(170, EventType.StageSeparation, "Stage separation", "Hot staging", null, null, null, null, DataQuality.Simulated, Source),
        };
        var config = new MissionConfig(info.MissionCode, info.Name, info.LaunchTimeUtc, info.Description, events);
        return new Mission(config, new LaunchClock(info.LaunchTimeUtc), [Vehicle(VehicleType.Starship, 600), Vehicle(VehicleType.SuperHeavy, 400)]);
    }

    public static Vehicle Vehicle(VehicleType type, double endOffset)
    {
        var points = new List<TrajectoryPoint>
        {
            new(0, 25.997, -97.157, 10, 0, DataQuality.Simulated, Source),
            new(endOffset / 2, 26.0, -96.5, 60_000, 1500, DataQuality.Simulated, Source),
            new(endOffset, 26.0, -95.0, 120_000, 3000, DataQuality.Simulated, Source),
        };
        var name = type == VehicleType.Starship ? "Starship" : "Super Heavy";
        return new Vehicle(name, type, null, [new FlightPath(TrajectoryType.Planned, DataQuality.Simulated, Source, null, points)]);
    }

    public static TelemetrySample Sample(VehicleType vehicle, DateTimeOffset t0, double offset, DateTimeOffset receivedAt) =>
        new(vehicle, t0.AddSeconds(offset), receivedAt, 25.997, -97.157, 1000, 100, 90, 80, 0, DataQuality.Simulated, SimSource);

    public static FlightPath ActualPath(double endOffset) =>
        new(TrajectoryType.Actual, DataQuality.Simulated, SimSource, null,
        [
            new TrajectoryPoint(0, 25.997, -97.157, 10, 0, DataQuality.Simulated, SimSource),
            new TrajectoryPoint(endOffset, 26.0, -96.9, 5000, 300, DataQuality.Simulated, SimSource),
        ]);
}
