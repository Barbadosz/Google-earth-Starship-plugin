using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Tests;

public class ModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly double[] NonFinite = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];

    private static TrajectoryPoint Point(double t, double lat = 25.99, double lon = -97.15, DataQuality q = DataQuality.Reconstructed) =>
        new(t, lat, lon, 100, null, q, "test");

    private static FlightPath Path(TrajectoryType type = TrajectoryType.Planned, DataQuality q = DataQuality.Reconstructed, params TrajectoryPoint[] points) =>
        new(type, q, "src", null, points.Length == 0 ? [Point(0), Point(1)] : points);

    private static Vehicle Veh(VehicleType type = VehicleType.Starship, string? model = null) =>
        new("V", type, model, [Path()]);

    // ---- TrajectoryPoint

    [Theory]
    [InlineData(90.0001, 0)]
    [InlineData(-90.0001, 0)]
    [InlineData(0, 180.0001)]
    [InlineData(0, -180.0001)]
    public void TrajectoryPoint_OutOfRange_Throws(double lat, double lon)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Point(0, lat, lon));
    }

    [Fact]
    public void TrajectoryPoint_Boundaries_Accepted_And_Longitude180Normalised()
    {
        Assert.Equal(90, Point(0, 90, 0).Latitude);
        Assert.Equal(-90, Point(0, -90, 0).Latitude);
        Assert.Equal(-180, Point(0, 0, -180).Longitude);
        Assert.Equal(-180, Point(0, 0, 180).Longitude);
        Assert.Equal(179.999, Point(0, 0, 179.999).Longitude);
    }

    [Fact]
    public void TrajectoryPoint_NonFiniteOnEveryDouble_Throws()
    {
        foreach (var bad in NonFinite)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectoryPoint(bad, 0, 0, 0, null, DataQuality.Unknown, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectoryPoint(0, bad, 0, 0, null, DataQuality.Unknown, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectoryPoint(0, 0, bad, 0, null, DataQuality.Unknown, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectoryPoint(0, 0, 0, bad, null, DataQuality.Unknown, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectoryPoint(0, 0, 0, 0, bad, DataQuality.Unknown, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectoryPoint(0, 0, 0, 0, null, DataQuality.Unknown, "s", headingDegrees: bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectoryPoint(0, 0, 0, 0, null, DataQuality.Unknown, "s", pitchDegrees: bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectoryPoint(0, 0, 0, 0, null, DataQuality.Unknown, "s", rollDegrees: bad));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(360, 0)]
    [InlineData(370, 10)]
    [InlineData(-10, 350)]
    [InlineData(-360, 0)]
    [InlineData(359.5, 359.5)]
    [InlineData(-1e-20, 0)]
    public void TrajectoryPoint_Heading_NormalisedToZero360(double input, double expected)
    {
        var p = new TrajectoryPoint(0, 0, 0, 0, null, DataQuality.Unknown, "s", headingDegrees: input);
        Assert.Equal(expected, p.HeadingDegrees!.Value, 9);
        Assert.InRange(p.HeadingDegrees!.Value, 0, 359.9999999);
    }

    [Theory]
    [InlineData(90.01, 0)]
    [InlineData(-90.01, 0)]
    [InlineData(0, 180.01)]
    [InlineData(0, -180.01)]
    public void TrajectoryPoint_PitchRollOutOfRange_Throws(double pitch, double roll)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrajectoryPoint(0, 0, 0, 0, null, DataQuality.Unknown, "s", null, pitch, roll));
    }

    [Fact]
    public void TrajectoryPoint_Orientation_StoredAndFlagged()
    {
        var p = new TrajectoryPoint(0, 0, 0, 0, null, DataQuality.Unknown, "s", 90, -90, 180);
        Assert.True(p.HasOrientation);
        Assert.Equal(-90, p.PitchDegrees);
        Assert.Equal(180, p.RollDegrees);
        Assert.False(Point(0).HasOrientation);
    }

    [Fact]
    public void TrajectoryPoint_BlankSource_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TrajectoryPoint(0, 0, 0, 0, null, DataQuality.Unknown, " "));
    }

    [Fact]
    public void TrajectoryPoint_NegativeOffset_IsAllowed()
    {
        Assert.Equal(-10, Point(-10).OffsetSeconds);
    }

    [Theory]
    [InlineData(DataQuality.Simulated, true)]
    [InlineData(DataQuality.Estimated, true)]
    [InlineData(DataQuality.Reconstructed, true)]
    [InlineData(DataQuality.Unknown, true)]
    [InlineData(DataQuality.Published, false)]
    [InlineData(DataQuality.Measured, false)]
    public void TrajectoryPoint_IsEstimated_FollowsQuality(DataQuality quality, bool expected)
    {
        Assert.Equal(expected, Point(0, q: quality).IsEstimated);
    }

    // ---- DataQuality ordering

    [Fact]
    public void DataQuality_Ranking_IsExplicitAndIncreasing()
    {
        DataQuality[] order =
        [
            DataQuality.Unknown, DataQuality.Simulated, DataQuality.Estimated,
            DataQuality.Reconstructed, DataQuality.Published, DataQuality.Measured,
        ];
        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(order[i].IsBetterThan(order[i - 1]));
            Assert.False(order[i - 1].IsBetterThan(order[i]));
        }

        Assert.Equal(DataQuality.Estimated, DataQualityExtensions.Worst(DataQuality.Measured, DataQuality.Estimated));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((DataQuality)99).Rank());
    }

    // ---- FlightPath

    [Fact]
    public void FlightPath_UnorderedOrDuplicateOffsets_Throw()
    {
        Assert.Throws<ArgumentException>(() => Path(points: [Point(10), Point(5)]));
        Assert.Throws<ArgumentException>(() => Path(points: [Point(5), Point(5)]));
    }

    [Fact]
    public void FlightPath_StrictlyIncreasing_Accepted()
    {
        Assert.Equal(3, Path(points: [Point(-1), Point(0), Point(5)]).Points.Count);
    }

    [Fact]
    public void FlightPath_QualityBetterThanWorstPoint_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Path(TrajectoryType.Actual, DataQuality.Measured, Point(0, q: DataQuality.Measured), Point(1, q: DataQuality.Estimated)));
        Assert.Throws<ArgumentException>(() =>
            Path(TrajectoryType.Actual, DataQuality.Published, Point(0, q: DataQuality.Reconstructed)));
    }

    [Fact]
    public void FlightPath_QualityEqualOrWorseThanPoints_Accepted()
    {
        Assert.Equal(DataQuality.Estimated,
            Path(TrajectoryType.Actual, DataQuality.Estimated, Point(0, q: DataQuality.Measured), Point(1, q: DataQuality.Estimated)).Quality);
        Assert.Equal(DataQuality.Measured,
            Path(TrajectoryType.Actual, DataQuality.Measured, Point(0, q: DataQuality.Measured)).Quality);
    }

    [Fact]
    public void FlightPath_TypeQualityConsistency()
    {
        Assert.Throws<ArgumentException>(() => Path(TrajectoryType.Planned, DataQuality.Measured, Point(0, q: DataQuality.Measured)));
        Assert.Throws<ArgumentException>(() => Path(TrajectoryType.Reconstructed, DataQuality.Published, Point(0, q: DataQuality.Published)));
        Assert.Throws<ArgumentException>(() => Path(TrajectoryType.Reconstructed, DataQuality.Measured, Point(0, q: DataQuality.Measured)));
        Assert.NotNull(Path(TrajectoryType.Planned, DataQuality.Published, Point(0, q: DataQuality.Published)));
    }

    [Fact]
    public void FlightPath_NullElementOrList_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FlightPath(TrajectoryType.Planned, DataQuality.Unknown, "s", null, [Point(0), null!]));
        Assert.Throws<ArgumentNullException>(() => new FlightPath(TrajectoryType.Planned, DataQuality.Unknown, "s", null, null!));
        Assert.Throws<ArgumentException>(() => new FlightPath(TrajectoryType.Planned, DataQuality.Unknown, " ", null, []));
    }

    [Fact]
    public void FlightPath_CopiesInputList()
    {
        var list = new List<TrajectoryPoint> { Point(0), Point(1) };
        var path = new FlightPath(TrajectoryType.Planned, DataQuality.Reconstructed, "s", null, list);
        list.Add(Point(2));
        Assert.Equal(2, path.Points.Count);
        Assert.False(path.Points is TrajectoryPoint[] or List<TrajectoryPoint>);
    }

    [Fact]
    public void FlightPath_TypeUsableFromOtherNamespaceWithoutConflict()
    {
        // The namespace StarshipMissionTracker.Trajectory exists; FlightPath must not collide with it.
        StarshipMissionTracker.Core.Models.FlightPath p = Path();
        Assert.NotNull(p);
    }

    // ---- MissionEvent

    [Theory]
    [InlineData(91.0, 0.0)]
    [InlineData(-91.0, 0.0)]
    [InlineData(0.0, 181.0)]
    [InlineData(0.0, -181.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.PositiveInfinity)]
    public void MissionEvent_PositionOutOfRange_Throws(double lat, double lon)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MissionEvent(0, EventType.Liftoff, "e", null, lat, lon, null, null, DataQuality.Published, "s"));
    }

    [Fact]
    public void MissionEvent_NonFiniteOffsetOrAltitude_Throws()
    {
        foreach (var bad in NonFinite)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MissionEvent(bad, EventType.Liftoff, "e", null, null, null, null, null, DataQuality.Published, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MissionEvent(0, EventType.Liftoff, "e", null, 0, 0, bad, null, DataQuality.Published, "s"));
        }
    }

    [Fact]
    public void MissionEvent_BoundariesAccepted_Longitude180Normalised()
    {
        var e = new MissionEvent(0, EventType.Liftoff, "e", null, -90, 180, 0, null, DataQuality.Published, "s");
        Assert.Equal(-180, e.Longitude);
        Assert.Equal(-90, e.Latitude);
    }

    [Fact]
    public void MissionEvent_LongitudeWithoutLatitude_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new MissionEvent(0, EventType.Liftoff, "e", null, null, 1.0, null, null, DataQuality.Published, "s"));
        Assert.Throws<ArgumentException>(() =>
            new MissionEvent(0, EventType.Liftoff, "e", null, 25.0, null, null, null, DataQuality.Published, "s"));
    }

    [Fact]
    public void MissionEvent_BlankNameOrSource_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new MissionEvent(0, EventType.Liftoff, " ", null, null, null, null, null, DataQuality.Published, "s"));
        Assert.Throws<ArgumentException>(() =>
            new MissionEvent(0, EventType.Liftoff, "e", null, null, null, null, null, DataQuality.Published, ""));
    }

    [Fact]
    public void MissionEvent_NoPosition_HasPositionFalse()
    {
        var e = new MissionEvent(62, EventType.MaxQ, "Max Q", null, null, null, null, null, DataQuality.Published, "src");
        Assert.False(e.HasPosition);
    }

    // ---- Vehicle

    [Fact]
    public void Vehicle_Validation()
    {
        Assert.Throws<ArgumentException>(() => new Vehicle(" ", VehicleType.Starship, null, [Path()]));
        Assert.Throws<ArgumentException>(() => new Vehicle("V", VehicleType.Starship, null, []));
        Assert.Throws<ArgumentException>(() => new Vehicle("V", VehicleType.Starship, null, [null!]));
        Assert.Throws<ArgumentNullException>(() => new Vehicle("V", VehicleType.Starship, null, null!));
    }

    [Theory]
    [InlineData("../evil.dae")]
    [InlineData("files/../../evil.dae")]
    [InlineData("/abs.dae")]
    [InlineData("C:/abs.dae")]
    [InlineData("files\\starship.dae")]
    [InlineData("files//x.dae")]
    [InlineData("")]
    [InlineData(" ")]
    public void Vehicle_BadModelPath_Throws(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => Veh(model: path));
    }

    [Fact]
    public void Vehicle_GoodModelPath_AndNull_Accepted()
    {
        Assert.Equal("files/starship.dae", Veh(model: "files/starship.dae").ModelPath);
        Assert.Null(Veh().ModelPath);
    }

    // ---- KmzEntry

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\x.dae")]
    [InlineData("C:/x.dae")]
    [InlineData("..")]
    [InlineData("../x.dae")]
    [InlineData("a/../../x.dae")]
    [InlineData("a\\b.dae")]
    [InlineData("a/./b.dae")]
    [InlineData("a//b.dae")]
    [InlineData("a/")]
    [InlineData("doc.kml")]
    [InlineData("DOC.KML")]
    public void KmzEntry_BadPath_Throws(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => new KmzEntry(path, new byte[] { 1 }));
    }

    [Fact]
    public void KmzEntry_NullPath_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new KmzEntry(null!, new byte[] { 1 }));
    }

    [Fact]
    public void KmzEntry_NullContent_Throws()
    {
        // Null array is rejected (no silent empty content).
        Assert.Throws<ArgumentNullException>(() => new KmzEntry("files/a.dae", (byte[])null!));
    }

    [Fact]
    public void KmzEntry_CopiesContent()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var entry = new KmzEntry("files/a.dae", bytes);
        bytes[0] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, entry.Content.ToArray());
        Assert.Equal("files/a.dae", entry.RelativePath);
    }

    // ---- Densification options

    [Fact]
    public void DensificationOptions_Default_Is10kmAnd10s()
    {
        Assert.Equal(10_000, TrajectoryDensificationOptions.Default.MaxSpacingMeters);
        Assert.Equal(10, TrajectoryDensificationOptions.Default.MaxSpacingSeconds);
    }

    [Theory]
    [InlineData(0.0, 10.0)]
    [InlineData(-1.0, 10.0)]
    [InlineData(double.NaN, 10.0)]
    [InlineData(double.PositiveInfinity, 10.0)]
    [InlineData(10.0, 0.0)]
    [InlineData(10.0, -5.0)]
    [InlineData(10.0, double.NaN)]
    [InlineData(10.0, double.PositiveInfinity)]
    public void DensificationOptions_Invalid_Throws(double meters, double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectoryDensificationOptions(meters, seconds));
    }

    // ---- MissionConfig

    [Fact]
    public void MissionConfig_NonUtcLaunchTime_Throws()
    {
        var local = new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.FromHours(1));
        Assert.Throws<ArgumentException>(() => new MissionConfig("IFT-X", "Test", local, null, []));
    }

    [Fact]
    public void MissionConfig_NullEventElement_Throws()
    {
        Assert.Throws<ArgumentException>(() => new MissionConfig("IFT-X", "Test", null, null, [null!]));
        Assert.Throws<ArgumentNullException>(() => new MissionConfig("IFT-X", "Test", null, null, null!));
    }

    [Fact]
    public void MissionConfig_CopiesEvents()
    {
        var events = new List<MissionEvent>
        {
            new(0, EventType.Liftoff, "Liftoff", null, null, null, null, null, DataQuality.Published, "s"),
        };
        var config = new MissionConfig("IFT-X", "Test", null, null, events);
        events.Clear();
        Assert.Single(config.Events);
    }

    [Fact]
    public void MissionConfig_WithLaunchTime_OverridesAndKeepsRest()
    {
        var config = new MissionConfig("IFT-X", "Test", null, "d", []);
        var updated = config.WithLaunchTime(T0);
        Assert.Null(config.LaunchTimeUtc);
        Assert.Equal(T0, updated.LaunchTimeUtc);
        Assert.Equal("IFT-X", updated.MissionCode);
    }

    // ---- Mission

    [Fact]
    public void Mission_ExposesCodeAndName_AndFillsConfigT0()
    {
        var config = new MissionConfig("IFT-X", "Test", null, null, []);
        var mission = new Mission(config, new LaunchClock(T0), []);
        Assert.Equal("IFT-X", mission.MissionCode);
        Assert.Equal("Test", mission.Name);
        Assert.Equal(T0, mission.Config.LaunchTimeUtc);
    }

    [Fact]
    public void Mission_ConfigAndClockDisagree_Throws()
    {
        var config = new MissionConfig("IFT-X", "Test", T0, null, []);
        Assert.Throws<ArgumentException>(() => new Mission(config, new LaunchClock(T0.AddHours(1)), []));
    }

    [Fact]
    public void Mission_DuplicateVehicleType_Throws()
    {
        var config = new MissionConfig("IFT-X", "Test", T0, null, []);
        Assert.Throws<ArgumentException>(() =>
            new Mission(config, new LaunchClock(T0), [Veh(VehicleType.Starship), Veh(VehicleType.Starship)]));
        var ok = new Mission(config, new LaunchClock(T0), [Veh(VehicleType.Starship), Veh(VehicleType.SuperHeavy)]);
        Assert.Equal(2, ok.Vehicles.Count);
    }

    [Fact]
    public void Mission_NullVehicleElement_Throws()
    {
        var config = new MissionConfig("IFT-X", "Test", T0, null, []);
        Assert.Throws<ArgumentException>(() => new Mission(config, new LaunchClock(T0), [null!]));
    }

    [Fact]
    public void MissionCreate_OverrideWinsOverConfig()
    {
        var config = new MissionConfig("IFT-X", "Test", T0, null, []);
        var over = T0.AddDays(3);
        var mission = Mission.Create(config, over, []);
        Assert.Equal(over, mission.Clock.LaunchTimeUtc);
        Assert.Equal(over, mission.Config.LaunchTimeUtc);
    }

    [Fact]
    public void MissionCreate_UsesConfigWhenNoOverride()
    {
        var config = new MissionConfig("IFT-X", "Test", T0, null, []);
        var mission = Mission.Create(config, null, []);
        Assert.Equal(T0, mission.Clock.LaunchTimeUtc);
        Assert.Equal(T0, mission.Config.LaunchTimeUtc);
    }

    [Fact]
    public void MissionCreate_OverrideOnlyWhenConfigHasNone()
    {
        var config = new MissionConfig("IFT-X", "Test", null, null, []);
        var mission = Mission.Create(config, T0, []);
        Assert.Equal(T0, mission.Config.LaunchTimeUtc);
        Assert.Equal(T0, mission.Clock.LaunchTimeUtc);
    }

    [Fact]
    public void MissionCreate_NoT0Anywhere_ThrowsMissionDataException()
    {
        var config = new MissionConfig("IFT-X", "Test", null, null, []);
        Assert.Throws<MissionDataException>(() => Mission.Create(config, null, []));
    }

    [Fact]
    public void MissionCreate_NonUtcOverride_Throws()
    {
        var config = new MissionConfig("IFT-X", "Test", null, null, []);
        Assert.Throws<ArgumentException>(() => Mission.Create(config, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(2)), []));
    }
}
