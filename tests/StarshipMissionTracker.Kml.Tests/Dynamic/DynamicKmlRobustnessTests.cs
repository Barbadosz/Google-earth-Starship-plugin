using System.Net;
using System.Text;
using System.Xml.Linq;
using StarshipMissionTracker.Cli.Options;
using StarshipMissionTracker.Cli.Pipeline;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Dynamic;
using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Kml.Tests.Schema;
using static StarshipMissionTracker.Kml.Tests.Dynamic.DynamicKmlTestData;

namespace StarshipMissionTracker.Kml.Tests.Dynamic;

/// <summary>N-27 review round 1: escaping, model path, Region/Lod, provenance labels and the real demo size budget.</summary>
public class DynamicKmlRobustnessTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly TimeSpan MinRefresh = TimeSpan.FromSeconds(2);
    private readonly DynamicKmlBuilder _builder = new();

    /// <summary>Every XML-special and HTML-special character, the CDATA terminator and illegal control characters.</summary>
    private const string Hostile = "A&B <b>\"q\"</b> ]]> end\u0001\u0008";

    /// <summary>What must survive: the same text without the illegal control characters.</summary>
    private const string HostileClean = "A&B <b>\"q\"</b> ]]> end";

    private static void AssertValid(string kml)
    {
        var problems = KmlSchemaValidator.Validate(kml);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(20)));
    }

    private static void AssertWellFormedAndClean(string kml)
    {
        Assert.DoesNotContain('\u0001', kml);
        Assert.DoesNotContain('\u0008', kml);
        _ = XDocument.Parse(kml);
        AssertValid(kml);
    }

    private static MissionInfo HostileInfo(bool simulated = true) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), Hostile, Hostile, Hostile,
            MissionStatus.InFlight, T0, T0, simulated, T0, (PadLat, PadLon));

    private static string DecodedDescription(XElement placemarkOrDocument) =>
        WebUtility.HtmlDecode(placemarkOrDocument.Element(K + "description")!.Value);

    // ---- escaping ----

    [Fact]
    public void Master_HostileNames_AreEscaped_AndRoundTrip()
    {
        var kml = _builder.BuildMaster(HostileInfo(), Urls, Settings);

        AssertWellFormedAndClean(kml);
        var document = XDocument.Parse(kml).Descendants(K + "Document").First();
        Assert.Contains(HostileClean, document.Element(K + "name")!.Value, StringComparison.Ordinal);
        Assert.Contains(HostileClean, DecodedDescription(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Planned_HostileMissionEventsAndControlUrl_StayWellFormed_AndRoundTrip()
    {
        var events = new List<MissionEvent>
        {
            new(0, EventType.Liftoff, Hostile, Hostile, PadLat, PadLon, 5, null, DataQuality.Simulated, Hostile),
        };
        var config = new MissionConfig("test", "Test mission", T0, "Illustrative test data.", events);
        var mission = new Mission(config, new LaunchClock(T0),
            [Starship(Path(TrajectoryType.Planned, 3)), SuperHeavy(Path(TrajectoryType.Planned, 3))]);
        var controlUrl = "http://localhost:5000/control?a=1&b=<2>\"]]>\u0001";

        var kml = _builder.BuildPlanned(HostileInfo(), mission, new HashSet<FlightPath>(), Assets, 1, MinRefresh, controlUrl);

        AssertWellFormedAndClean(kml);
        var doc = XDocument.Parse(kml);
        var info = doc.Descendants(K + "Folder").First().Descendants(K + "description").Single();
        Assert.Contains(HostileClean, WebUtility.HtmlDecode(info.Value), StringComparison.Ordinal);
        Assert.Contains("http://localhost:5000/control?a=1&b=<2>\"]]>", WebUtility.HtmlDecode(info.Value), StringComparison.Ordinal);

        var ev = doc.Descendants(K + "Placemark").Single(p => p.Element(K + "styleUrl")?.Value == "#event");
        Assert.Equal(HostileClean, ev.Element(K + "name")!.Value);
        Assert.Contains(HostileClean, DecodedDescription(ev), StringComparison.Ordinal);
        Assert.Equal(HostileClean, ev.Descendants(K + "Data").Single(d => d.Attribute("name")!.Value == "Source").Element(K + "value")!.Value);
    }

    [Fact]
    public void History_HostileMissionName_StaysWellFormed()
    {
        var kml = _builder.BuildHistory(HostileInfo(), [Starship(Path(TrajectoryType.Actual, 3))], Assets, 1, MinRefresh);

        AssertWellFormedAndClean(kml);
        Assert.Contains(HostileClean, XDocument.Parse(kml).Descendants(K + "name").First().Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_HostileNamesAndSource_StayWellFormed_AndRoundTrip()
    {
        var vehicle = new Vehicle(Hostile, VehicleType.Starship, "models/starship.dae", [Path(TrajectoryType.Planned, 2)]);
        var sample = new TelemetrySample(VehicleType.Starship, Now, Now, PadLat, PadLon, 100, 10, 90, 80, 0,
            DataQuality.Measured, Hostile);

        var kml = _builder.BuildLive(HostileInfo(simulated: false), [Live(vehicle, sample)], Now, Assets, 1, MinRefresh);

        AssertWellFormedAndClean(kml);
        var placemark = XDocument.Parse(kml).Descendants(K + "Placemark").First();
        Assert.StartsWith(HostileClean, placemark.Element(K + "name")!.Value, StringComparison.Ordinal);
        var description = DecodedDescription(placemark);
        Assert.Contains(HostileClean, description, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>\"q\"</b>", placemark.Element(K + "description")!.Value, StringComparison.Ordinal);
        Assert.Equal(HostileClean, placemark.Descendants(K + "Data").Single(d => d.Attribute("name")!.Value == "Source").Element(K + "value")!.Value);
    }

    // ---- model path ----

    private string LiveModelHref(Vehicle vehicle, KmlAssetUrls? assets = null)
    {
        var kml = _builder.BuildLive(Info(), [Live(vehicle, Sample())], Now, assets ?? Assets, 1, MinRefresh);
        AssertValid(kml);
        return XDocument.Parse(kml).Descendants(K + "Model").Single().Descendants(K + "href").Single().Value;
    }

    private static Vehicle StarshipAt(string? modelPath) =>
        new("Starship", VehicleType.Starship, modelPath, [Path(TrajectoryType.Planned, 2)]);

    [Theory]
    [InlineData("files/custom-ship.dae", "http://localhost:5000/assets/models/custom-ship.dae")]
    [InlineData("custom.dae", "http://localhost:5000/assets/models/custom.dae")]
    [InlineData("files/my ship.dae", "http://localhost:5000/assets/models/my%20ship.dae")]
    [InlineData(null, "http://localhost:5000/assets/models/starship.dae")]
    [InlineData("models/", "http://localhost:5000/assets/models/starship.dae")]
    public void Live_ModelPath_UsesLastSegment_OrDefaultFile(string? modelPath, string expectedHref)
    {
        Vehicle vehicle;
        try
        {
            vehicle = StarshipAt(modelPath);
        }
        catch (ArgumentException)
        {
            // The Core guard rejects this path shape; the generator is then never reached with it.
            Assert.Equal("models/", modelPath);
            return;
        }

        Assert.Equal(expectedHref, LiveModelHref(vehicle));
    }

    [Fact]
    public void Live_SuperHeavyWithoutModelPath_FallsBackToDefaultModelFile()
    {
        var vehicle = new Vehicle("Super Heavy", VehicleType.SuperHeavy, null, [Path(TrajectoryType.Planned, 2)]);

        Assert.Equal("http://localhost:5000/assets/models/superheavy.dae", LiveModelHref(vehicle));
    }

    [Theory]
    [InlineData("http://localhost:5000/assets/models/")]
    [InlineData("http://localhost:5000/assets/models")]
    [InlineData("http://localhost:5000/assets/models///")]
    public void Live_ModelBaseUrl_TrailingSlashes_NeverDoubleSlash(string baseUrl)
    {
        var href = LiveModelHref(StarshipAt("files/starship.dae"), new KmlAssetUrls(baseUrl, "http://localhost:5000/assets/icons/"));

        Assert.Equal("http://localhost:5000/assets/models/starship.dae", href);
        Assert.DoesNotContain("//", href[7..], StringComparison.Ordinal);
    }

    // ---- Region / Lod ----

    private const double BoxHalfSpan = 0.05; // LiveVehicleGenerator.ModelBoxHalfSpanDegrees (internal)
    private const double BoxHalfHeight = 1000; // LiveVehicleGenerator.ModelBoxHalfHeightMeters (internal)

    [Fact]
    public void Live_ModelRegion_BoxAroundSample_AndSharedM1Lod()
    {
        var kml = _builder.BuildLive(Info(), [Live(Starship(Path(TrajectoryType.Planned, 2)), Sample())], Now, Assets, 1, MinRefresh);
        AssertValid(kml);
        var region = XDocument.Parse(kml).Descendants(K + "Region").Single();
        var box = region.Element(K + "LatLonAltBox")!;
        double Num(string name) => double.Parse(box.Element(K + name)!.Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(Num("north") > Num("south"));
        Assert.True(Num("west") < Num("east"));
        Assert.True(Num("maxAltitude") > Num("minAltitude"));
        Assert.Equal(PadLat + BoxHalfSpan, Num("north"), 6);
        Assert.Equal(PadLat - BoxHalfSpan, Num("south"), 6);
        Assert.Equal(PadLon + BoxHalfSpan, Num("east"), 6);
        Assert.Equal(PadLon - BoxHalfSpan, Num("west"), 6);
        Assert.Equal(1234.56 + BoxHalfHeight, Num("maxAltitude"), 1);
        Assert.Equal(1234.56 - BoxHalfHeight, Num("minAltitude"), 1);
        var lod = region.Element(K + "Lod")!;
        Assert.Equal(KmlLod.ModelMinLodPixels.ToString(System.Globalization.CultureInfo.InvariantCulture), lod.Element(K + "minLodPixels")!.Value);
        Assert.Equal(KmlLod.ModelMaxLodPixels.ToString(System.Globalization.CultureInfo.InvariantCulture), lod.Element(K + "maxLodPixels")!.Value);
    }

    [Fact]
    public void Live_ModelRegion_NearPoleAndAntimeridian_StaysInRangeAndOrdered()
    {
        var kml = _builder.BuildLive(Info(), [Live(Starship(Path(TrajectoryType.Planned, 2)), Sample(lat: 89.99, lon: 179.99))], Now, Assets, 1, MinRefresh);
        AssertValid(kml);
        var box = XDocument.Parse(kml).Descendants(K + "LatLonAltBox").Single();
        double Num(string name) => double.Parse(box.Element(K + name)!.Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.InRange(Num("north"), -90, 90);
        Assert.InRange(Num("east"), -180, 180);
        Assert.True(Num("north") > Num("south"));
        Assert.True(Num("west") < Num("east"));
    }

    // ---- orientation ----

    [Theory]
    [InlineData(null, 80.0, 0.0)]
    [InlineData(90.0, null, 0.0)]
    [InlineData(90.0, 80.0, null)]
    [InlineData(null, 80.0, null)]
    public void Live_PartialOrientation_HasNoOrientationElement(double? heading, double? pitch, double? roll)
    {
        var kml = _builder.BuildLive(Info(),
            [Live(Starship(Path(TrajectoryType.Planned, 2)), Sample(heading: heading, pitch: pitch, roll: roll))],
            Now, Assets, 1, MinRefresh);

        AssertValid(kml);
        Assert.Empty(XDocument.Parse(kml).Descendants(K + "Orientation"));
    }

    // ---- provenance labels ----

    private string LiveWith(MissionInfo info, TelemetrySample sample, bool stale = false, double? ageSeconds = 1)
    {
        var kml = _builder.BuildLive(info, [Live(Starship(Path(TrajectoryType.Planned, 2)), sample, stale, ageSeconds)], Now, Assets, 1, MinRefresh);
        AssertValid(kml);
        return kml;
    }

    private static TelemetrySample WithQuality(DataQuality quality, string source = "Real feed") =>
        new(VehicleType.Starship, Now, Now, PadLat, PadLon, 10, 5, 90, 80, 0, quality, source);

    [Fact]
    public void Live_NotSimulatedMission_SimulatedQualitySample_IsStillLabelled()
    {
        var doc = XDocument.Parse(LiveWith(Info(simulated: false), WithQuality(DataQuality.Simulated)));

        var placemarks = doc.Descendants(K + "Placemark").ToList();
        Assert.All(placemarks, p => Assert.Contains("(SIMULATED)", p.Element(K + "name")!.Value, StringComparison.Ordinal));
        Assert.Contains("Simulated position – not live telemetry.", placemarks[0].Element(K + "description")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_NotSimulatedMission_EstimatedSample_SaysNotMeasured_AndIsNotLabelledSimulated()
    {
        var doc = XDocument.Parse(LiveWith(Info(simulated: false), WithQuality(DataQuality.Estimated)));

        var description = doc.Descendants(K + "Placemark").First().Element(K + "description")!.Value;
        Assert.Contains("Data quality: Estimated (not measured telemetry)", description, StringComparison.Ordinal);
        Assert.DoesNotContain("SIMULATED", doc.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Live_NotSimulatedMission_MeasuredSample_SaysMeasured_NotEstimated()
    {
        var doc = XDocument.Parse(LiveWith(Info(simulated: false), WithQuality(DataQuality.Measured)));

        var description = doc.Descendants(K + "Placemark").First().Element(K + "description")!.Value;
        Assert.Contains("Data quality: Measured", description, StringComparison.Ordinal);
        Assert.DoesNotContain("not measured", description, StringComparison.Ordinal);
        Assert.DoesNotContain("SIMULATED", doc.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Live_StaleWithoutAge_IsMarkedStale_AndHasNoAgeLine()
    {
        var doc = XDocument.Parse(LiveWith(Info(), Sample(), stale: true, ageSeconds: null));

        var placemark = doc.Descendants(K + "Placemark").First();
        Assert.EndsWith("– STALE", placemark.Element(K + "name")!.Value, StringComparison.Ordinal);
        var description = placemark.Element(K + "description")!.Value;
        Assert.Contains("STALE", description, StringComparison.Ordinal);
        Assert.DoesNotContain("Telemetry age", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_TPlusLabel_UsesPlannedLaunchTime_EvenWhenActualLaunchDiffers()
    {
        var info = new MissionInfo(Guid.NewGuid(), "test", "Test mission", null, MissionStatus.InFlight,
            T0, T0.AddSeconds(30), true, T0, (PadLat, PadLon));

        var doc = XDocument.Parse(LiveWith(info, Sample()));

        // Sample time is T0 + 100 s: the planned T0 is the one shared source, as in the planned document's events.
        Assert.Contains("T+00:01:40", doc.Descendants(K + "Placemark").First().Element(K + "description")!.Value, StringComparison.Ordinal);
    }

    // ---- size budget of the real demo mission ----

    private const int DemoPlannedBudgetBytes = 600_000;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "StarshipMissionTracker.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (.sln) not found.");
    }

    [Fact]
    public async Task Planned_DemoMission_StaysWithinBudget_AndIsSchemaValid()
    {
        var options = new CliOptions("demo-ift", T0,
            System.IO.Path.Combine(RepoRoot(), "data"), System.IO.Path.Combine(RepoRoot(), "assets"),
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unused-output"));
        var mission = await new MissionKmzPipeline(Serilog.Core.Logger.None)
            .LoadAndPrepareAsync(options, TestContext.Current.CancellationToken);
        var info = new MissionInfo(Guid.NewGuid(), mission.MissionCode, mission.Config.Name, null, MissionStatus.InFlight,
            mission.Clock.LaunchTimeUtc, null, true, mission.Clock.LaunchTimeUtc);

        var kml = _builder.BuildPlanned(info, mission, new HashSet<FlightPath>(), Assets, 1, MinRefresh);

        var bytes = Encoding.UTF8.GetByteCount(kml);
        Assert.True(bytes <= DemoPlannedBudgetBytes, $"demo planned document is {bytes} bytes");
        AssertValid(kml);
    }
}
