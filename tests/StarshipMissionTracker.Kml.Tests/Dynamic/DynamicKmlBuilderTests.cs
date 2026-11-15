using System.Globalization;
using System.Text;
using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Dynamic;
using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Kml.Tests.Schema;
using static StarshipMissionTracker.Kml.Tests.Dynamic.DynamicKmlTestData;

namespace StarshipMissionTracker.Kml.Tests.Dynamic;

/// <summary>N-27 / N-15 / N-20: the server-mode KML documents.</summary>
public class DynamicKmlBuilderTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly TimeSpan MinRefresh = TimeSpan.FromSeconds(2);
    private readonly DynamicKmlBuilder _builder = new();

    private static XDocument Parse(string kml) => XDocument.Parse(kml);

    private static void AssertValid(string kml)
    {
        var problems = KmlSchemaValidator.Validate(kml);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(20)));
    }

    private static void AssertNone(IEnumerable<XElement> elements)
    {
        var found = elements.Select(e => e.Name.LocalName).ToList();
        Assert.True(found.Count == 0, "Unexpected elements: " + string.Join(", ", found));
    }

    private string Master(bool simulated = true) => _builder.BuildMaster(Info(simulated), Urls, Settings);

    private string Planned(string? controlUrl = null, IReadOnlySet<FlightPath>? densified = null, Mission? mission = null, long revision = 7) =>
        _builder.BuildPlanned(Info(), mission ?? PlannedMission(), densified ?? new HashSet<FlightPath>(), Assets, revision, MinRefresh, controlUrl);

    private string History(IReadOnlyList<Vehicle>? vehicles = null, long revision = 8) =>
        _builder.BuildHistory(Info(), vehicles ?? [Starship(Path(TrajectoryType.Actual, 4))], Assets, revision, MinRefresh);

    private string LiveDoc(IReadOnlyList<LiveVehicleState>? states = null, long revision = 9) =>
        _builder.BuildLive(Info(), states ?? [Live(Starship(Path(TrajectoryType.Planned, 2)), Sample())], Now, Assets, revision, MinRefresh);

    // ---- schema ----

    [Fact]
    public void AllFourDocuments_ValidateAgainstKmlSchema()
    {
        AssertValid(Master());
        AssertValid(Master(simulated: false));
        AssertValid(Planned("http://localhost:5000/control"));
        AssertValid(Planned());
        AssertValid(History());
        AssertValid(History([]));
        AssertValid(LiveDoc());
        AssertValid(LiveDoc([
            Live(Starship(Path(TrajectoryType.Planned, 2)), null, ageSeconds: null),
            Live(SuperHeavy(Path(TrajectoryType.Planned, 2)), Sample(VehicleType.SuperHeavy, heading: null, pitch: 70, roll: null, alt: null), stale: true, ageSeconds: 30)]));
    }

    // ---- master ----

    [Fact]
    public void Master_ThreeNetworkLinks_WithRefreshSettings()
    {
        var links = Parse(Master()).Descendants(K + "NetworkLink").ToList();

        Assert.Equal(3, links.Count);
        var expected = new[]
        {
            (Url: Urls.PlannedUrl, Interval: "60"),
            (Url: Urls.HistoryUrl, Interval: "15"),
            (Url: Urls.LiveUrl, Interval: "5"),
        };
        for (var i = 0; i < 3; i++)
        {
            var link = links[i];
            Assert.Equal("0", link.Element(K + "flyToView")!.Value);
            Assert.Equal("0", link.Element(K + "refreshVisibility")!.Value);
            Assert.Equal("1", link.Element(K + "visibility")!.Value);
            var l = link.Element(K + "Link")!;
            Assert.Equal(expected[i].Url, l.Element(K + "href")!.Value);
            Assert.Equal("onInterval", l.Element(K + "refreshMode")!.Value);
            Assert.Equal(expected[i].Interval, l.Element(K + "refreshInterval")!.Value);
            Assert.Null(l.Element(K + "viewRefreshMode"));
        }
    }

    [Fact]
    public void Master_NoTimeElements_AndNoCookie_AndLookAtOverLaunchSite()
    {
        var doc = Parse(Master());

        AssertNone(doc.Descendants().Where(e => e.Name.LocalName is "TimeStamp" or "TimeSpan"));
        Assert.Empty(doc.Descendants(K + "NetworkLinkControl"));
        var lookAt = doc.Descendants(K + "LookAt").Single();
        Assert.Equal("-97.156600", lookAt.Element(K + "longitude")!.Value);
        Assert.Equal("25.997200", lookAt.Element(K + "latitude")!.Value);
    }

    [Fact]
    public void Master_WithoutLaunchSite_HasNoLookAt()
    {
        var kml = _builder.BuildMaster(Info(launchSite: false), Urls, Settings);

        Assert.Empty(Parse(kml).Descendants(K + "LookAt"));
        AssertValid(kml);
    }

    [Fact]
    public void Master_SimulatedLabel_OnlyWhenSimulated()
    {
        var simulated = Parse(Master()).Descendants(K + "NetworkLink").Select(l => l.Element(K + "name")!.Value).ToList();
        var real = Parse(Master(simulated: false)).Descendants(K + "NetworkLink").Select(l => l.Element(K + "name")!.Value).ToList();

        Assert.Contains("SIMULATED", simulated[1]);
        Assert.Contains("SIMULATED", simulated[2]);
        Assert.DoesNotContain("SIMULATED", string.Concat(real));
    }

    // ---- cookie / revision / cache busting ----

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(1234567890123)]
    public void ChildDocuments_CarryCookieWithRevision_AndMinRefreshPeriod(long revision)
    {
        foreach (var kml in new[] { Planned(revision: revision), History(revision: revision), LiveDoc(revision: revision) })
        {
            var doc = Parse(kml);
            var control = doc.Root!.Elements().First();
            Assert.Equal(K + "NetworkLinkControl", control.Name);
            Assert.Equal("2", control.Element(K + "minRefreshPeriod")!.Value);
            Assert.Equal("r=" + revision.ToString(CultureInfo.InvariantCulture), control.Element(K + "cookie")!.Value);
            Assert.Single(doc.Descendants(K + "NetworkLinkControl"));
        }
    }

    [Fact]
    public void Revision_ChangesTheDocument_ButNothingElse()
    {
        var a = LiveDoc(revision: 1);
        var b = LiveDoc(revision: 2);

        Assert.NotEqual(a, b);
        Assert.Equal(a.Replace("r=1", "r=2", StringComparison.Ordinal), b);
    }

    [Fact]
    public void InvalidArguments_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveDoc(revision: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _builder.BuildHistory(Info(), [], Assets, 1, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() =>
            _builder.BuildLive(Info(), [], Now.ToOffset(TimeSpan.FromHours(2)), Assets, 1, MinRefresh));
    }

    // ---- planned ----

    [Fact]
    public void Planned_Structure_NoTimeElements_EachPathOnce()
    {
        var doc = Parse(Planned());

        var folders = doc.Descendants(K + "Document").Single().Elements(K + "Folder").Select(f => f.Element(K + "name")!.Value).ToList();
        Assert.Equal(["Mission Information", "Planned Trajectory", "Mission Events"], folders);
        AssertNone(doc.Descendants().Where(e => e.Name.LocalName is "TimeStamp" or "TimeSpan" or "Track"));
        Assert.Equal(2, doc.Descendants(K + "LineString").Count());
        Assert.Equal(3, doc.Descendants(K + "Placemark").Count(p => p.Element(K + "styleUrl")?.Value == "#event"));
        Assert.All(doc.Descendants(K + "LineString"), ls => Assert.Equal("absolute", ls.Element(K + "altitudeMode")!.Value));
    }

    [Fact]
    public void Planned_ActualPathsAreNotRepeated_PlannedAndReconstructedBothAppearOnce()
    {
        var mission = Mission([Starship(
            Path(TrajectoryType.Planned, 3), Path(TrajectoryType.Reconstructed, 3), Path(TrajectoryType.Actual, 3))]);

        Assert.Equal(2, Parse(Planned(mission: mission)).Descendants(K + "LineString").Count());
    }

    [Fact]
    public void Planned_Events_ShowAbsoluteTimeInDescription()
    {
        var doc = Parse(Planned());
        var separation = doc.Descendants(K + "Placemark").Single(p => p.Element(K + "name")?.Value == "Stage Separation");

        Assert.Contains("2026-12-01T14:02:40.500Z", separation.Element(K + "description")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Planned_ControlUrl_PresentOrAbsent_AsPlainText()
    {
        var with = Parse(Planned("http://localhost:5000/control/index.html?a=1&b=2"));
        var without = Parse(Planned());

        var info = with.Descendants(K + "Folder").First().Descendants(K + "description").Single().Value;
        Assert.Contains("Simulation control: http://localhost:5000/control/index.html?a=1&amp;b=2", info, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", info, StringComparison.Ordinal);
        Assert.DoesNotContain("Simulation control", without.Descendants(K + "Folder").First().Descendants(K + "description").Single().Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Simulation control", Planned("  "), StringComparison.Ordinal);
    }

    [Fact]
    public void Planned_SimulatedNotice_IsInMissionInformation_OnlyWhenSimulated()
    {
        var simulated = Planned();
        var real = _builder.BuildPlanned(Info(simulated: false), PlannedMission(), new HashSet<FlightPath>(), Assets, 1, MinRefresh);

        Assert.Contains("SIMULATED", simulated, StringComparison.Ordinal);
        Assert.DoesNotContain("SIMULATED", real, StringComparison.Ordinal);
    }

    [Fact]
    public void Planned_DensifiedPath_GetsNote()
    {
        var path = Path(TrajectoryType.Planned, 4);
        var mission = Mission([Starship(path), SuperHeavy(Path(TrajectoryType.Planned, 4))]);

        var doc = Parse(Planned(densified: new HashSet<FlightPath> { path }, mission: mission));

        Assert.Equal(1, doc.Descendants(K + "description").Count(d => d.Value.Contains("Densified for display", StringComparison.Ordinal)));
    }

    [Fact]
    public void Planned_IconStyles_UseIconBaseUrl()
    {
        var hrefs = Parse(Planned()).Descendants(K + "IconStyle").SelectMany(i => i.Descendants(K + "href")).Select(h => h.Value).ToList();

        Assert.Contains("http://localhost:5000/assets/icons/starship.png", hrefs);
        Assert.Contains("http://localhost:5000/assets/icons/booster.png", hrefs);
    }

    // ---- history ----

    [Fact]
    public void History_OnlyActualPaths_EstimatedStyle_NoTime()
    {
        var vehicles = new[]
        {
            Starship(Path(TrajectoryType.Planned, 3), Path(TrajectoryType.Actual, 4)),
            SuperHeavy(Path(TrajectoryType.Actual, 4)),
        };

        var doc = Parse(History(vehicles));

        Assert.Equal(2, doc.Descendants(K + "LineString").Count());
        AssertNone(doc.Descendants().Where(e => e.Name.LocalName is "TimeStamp" or "TimeSpan" or "Track"));
        Assert.All(doc.Descendants(K + "Placemark"), p => Assert.StartsWith("#estimated-", p.Element(K + "styleUrl")!.Value, StringComparison.Ordinal));
        Assert.Contains("Actual path, estimated", doc.ToString(), StringComparison.Ordinal);
        Assert.Contains("SIMULATED", doc.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void History_WithoutActualPath_IsAnEmptyValidFolder()
    {
        var kml = History([Starship(Path(TrajectoryType.Planned, 3))]);

        Assert.Empty(Parse(kml).Descendants(K + "Placemark"));
        AssertValid(kml);
    }

    [Fact]
    public void History_KnownPoint_InLineString()
    {
        var coordinates = Parse(History()).Descendants(K + "coordinates").First().Value;

        Assert.StartsWith("-97.156600,25.997200,5.0 ", coordinates, StringComparison.Ordinal);
    }

    // ---- live ----

    [Fact]
    public void Live_HasNoTimeElements_AndNoTrack()
    {
        var doc = Parse(LiveDoc());

        AssertNone(doc.Descendants().Where(e => e.Name.LocalName is "TimeStamp" or "TimeSpan" or "Track" or "when"));
    }

    [Fact]
    public void Live_ModelAndIcon_AtLatestSample_KnownPoint()
    {
        var doc = Parse(LiveDoc());

        var location = doc.Descendants(K + "Location").Single();
        Assert.Equal("-97.156600", location.Element(K + "longitude")!.Value);
        Assert.Equal("25.997200", location.Element(K + "latitude")!.Value);
        Assert.Equal("1234.6", location.Element(K + "altitude")!.Value);
        Assert.Equal("absolute", doc.Descendants(K + "Model").Single().Element(K + "altitudeMode")!.Value);
        Assert.Equal("http://localhost:5000/assets/models/starship.dae", doc.Descendants(K + "Model").Single().Descendants(K + "href").Single().Value);
        Assert.Equal("-97.156600,25.997200,1234.6", doc.Descendants(K + "Point").Single().Element(K + "coordinates")!.Value);
        Assert.Single(doc.Descendants(K + "Region"));
    }

    [Fact]
    public void Live_Orientation_TiltIs90MinusPitch_AndOmittedWithoutAngles()
    {
        var with = Parse(LiveDoc()).Descendants(K + "Orientation").Single();
        var without = Parse(LiveDoc([Live(Starship(Path(TrajectoryType.Planned, 2)), Sample(heading: null, pitch: null, roll: null))]));

        Assert.Equal("90.00", with.Element(K + "heading")!.Value);
        Assert.Equal("10.00", with.Element(K + "tilt")!.Value);
        Assert.Equal("0.00", with.Element(K + "roll")!.Value);
        Assert.Empty(without.Descendants(K + "Orientation"));
    }

    [Fact]
    public void Live_SimulatedLabel_InNameAndDescription()
    {
        var doc = Parse(LiveDoc());

        var names = doc.Descendants(K + "Placemark").Select(p => p.Element(K + "name")!.Value).ToList();
        Assert.Equal(["Starship (SIMULATED)", "Starship (SIMULATED) icon"], names);
        Assert.Contains("Simulated position – not live telemetry.", doc.Descendants(K + "Placemark").First().Element(K + "description")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_NotSimulated_HasNoSimulatedLabel()
    {
        var kml = _builder.BuildLive(Info(simulated: false), [Live(Starship(Path(TrajectoryType.Planned, 2)), new TelemetrySample(
            VehicleType.Starship, Now, Now, PadLat, PadLon, 10, null, null, null, null, DataQuality.Measured, "Real feed"))],
            Now, Assets, 1, MinRefresh);

        Assert.DoesNotContain("SIMULATED", kml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Live_StaleSample_IsMarked()
    {
        var doc = Parse(LiveDoc([Live(Starship(Path(TrajectoryType.Planned, 2)), Sample(), stale: true, ageSeconds: 42)]));

        var placemark = doc.Descendants(K + "Placemark").First();
        Assert.EndsWith("– STALE", placemark.Element(K + "name")!.Value, StringComparison.Ordinal);
        Assert.Contains("STALE", placemark.Element(K + "description")!.Value, StringComparison.Ordinal);
        Assert.Contains("Telemetry age: 42 s", placemark.Element(K + "description")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_VehicleWithoutSample_ShowsWaiting_AndNoPlacemark()
    {
        var doc = Parse(LiveDoc([Live(Starship(Path(TrajectoryType.Planned, 2)), null, ageSeconds: null)]));

        Assert.Empty(doc.Descendants(K + "Placemark"));
        Assert.Contains("Waiting for liftoff", doc.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Live_BothVehicles_SeparateFolders_StarshipFirst()
    {
        var doc = Parse(LiveDoc([
            Live(SuperHeavy(Path(TrajectoryType.Planned, 2)), Sample(VehicleType.SuperHeavy, lat: 26.1)),
            Live(Starship(Path(TrajectoryType.Planned, 2)), Sample())]));

        var folders = doc.Descendants(K + "Folder").Skip(1).Select(f => f.Element(K + "name")!.Value).ToList();
        Assert.Equal(["Starship", "Super Heavy"], folders);
        Assert.Equal(4, doc.Descendants(K + "Placemark").Count());
        Assert.Contains("models/superheavy.dae", doc.ToString(), StringComparison.Ordinal);
    }

    // ---- culture ----

    [Fact]
    public void AllDocuments_AreIdenticalUnderHuHuCulture_AndUseDotDecimals()
    {
        string[] Build() =>
        [
            Master(), Planned("http://localhost:5000/control"), History(), LiveDoc(),
        ];

        var invariant = WithCulture(CultureInfo.InvariantCulture, Build);
        var hungarian = WithCulture(new CultureInfo("hu-HU"), Build);

        Assert.Equal(invariant, hungarian);
        Assert.Contains("-97.156600,25.997200,1234.6", hungarian[3], StringComparison.Ordinal);
        Assert.DoesNotContain("1234,6", hungarian[3], StringComparison.Ordinal);
    }

    private static T WithCulture<T>(CultureInfo culture, Func<T> action)
    {
        var (old, oldUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
            CultureInfo.CurrentUICulture = oldUi;
        }
    }

    // ---- size budget (N-20) ----

    /// <summary>Budgets (UTF-8 bytes, uncompressed) for the documents the Api serves; raise deliberately, not by accident.</summary>
    private const int PlannedBudgetBytes = 600_000;
    private const int HistoryBudgetBytes = 250_000; // 2 vehicles x 2000 points
    private const int LiveBudgetBytes = 20_000;

    [Fact]
    public void History_TwoThousandPointsPerVehicle_StaysWithinBudget_AndPathIsWrittenOnce()
    {
        var kml = History([Starship(Path(TrajectoryType.Actual, 2000)), SuperHeavy(Path(TrajectoryType.Actual, 2000))]);

        Assert.True(Encoding.UTF8.GetByteCount(kml) <= HistoryBudgetBytes, $"history is {Encoding.UTF8.GetByteCount(kml)} bytes");
        Assert.Equal(2, Parse(kml).Descendants(K + "LineString").Count());
        Assert.Equal(4000, Parse(kml).Descendants(K + "coordinates").Sum(c => c.Value.Split(' ').Length));
    }

    [Fact]
    public void Planned_LargePaths_StayWithinBudget_AndEachPointIsWrittenOnce()
    {
        var kml = Planned(mission: Mission([Starship(Path(TrajectoryType.Planned, 3000)), SuperHeavy(Path(TrajectoryType.Planned, 3000))]));

        Assert.True(Encoding.UTF8.GetByteCount(kml) <= PlannedBudgetBytes, $"planned is {Encoding.UTF8.GetByteCount(kml)} bytes");
        Assert.Equal(6000, Parse(kml).Descendants(K + "LineString").Sum(ls => ls.Element(K + "coordinates")!.Value.Split(' ').Length));
    }

    [Fact]
    public void Live_StaysWithinBudget()
    {
        var kml = LiveDoc([
            Live(Starship(Path(TrajectoryType.Planned, 2)), Sample()),
            Live(SuperHeavy(Path(TrajectoryType.Planned, 2)), Sample(VehicleType.SuperHeavy))]);

        Assert.True(Encoding.UTF8.GetByteCount(kml) <= LiveBudgetBytes, $"live is {Encoding.UTF8.GetByteCount(kml)} bytes");
    }
}
