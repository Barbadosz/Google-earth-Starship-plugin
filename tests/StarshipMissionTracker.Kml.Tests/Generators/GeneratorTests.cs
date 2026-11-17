using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Kml.Tests.Generators;

public class GeneratorTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly XNamespace Gx = KmlXml.Gx;

    private static readonly LaunchClock Clock = new(new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.Zero));

    private static TrajectoryPoint Pt(
        double t, double lat, double lon, double alt, double? heading = null, double? pitch = null, double? roll = null) =>
        new(t, lat, lon, alt, null, DataQuality.Reconstructed, "test source", heading, pitch, roll);

    private static FlightPath Path(params TrajectoryPoint[] points) =>
        new(TrajectoryType.Planned, DataQuality.Reconstructed, "test source", "CC0", points);

    private static Vehicle Starship(FlightPath path, string? model = "files/starship.dae") =>
        new("Starship", VehicleType.Starship, model, [path]);

    private static FlightPath DefaultPath() => Path(
        Pt(0, 25.997, -97.157, 10.25),
        Pt(60, 26.1, -96.5, 12345.67),
        Pt(120, 26.5, -95.0, 50000));

    private static string Text(XElement e) => e.ToString(SaveOptions.DisableFormatting);

    [Fact]
    public void PlannedPath_Coordinates_LonLatAltInvariant()
    {
        var path = DefaultPath();
        var placemark = PlannedPathGenerator.CreatePlacemark(Starship(path), path);

        var coords = placemark.Element(K + "LineString")!.Element(K + "coordinates")!.Value;
        Assert.Equal("-97.157000,25.997000,10.3 -96.500000,26.100000,12345.7 -95.000000,26.500000,50000.0", coords);
    }

    [Fact]
    public void PlannedPath_LineString_ExtrudeTessellateAbsolute_NoTime()
    {
        var path = DefaultPath();
        var placemark = PlannedPathGenerator.CreatePlacemark(Starship(path), path);
        var line = placemark.Element(K + "LineString")!;

        Assert.Equal("1", line.Element(K + "extrude")!.Value);
        Assert.Equal("0", line.Element(K + "tessellate")!.Value);
        Assert.Equal("absolute", line.Element(K + "altitudeMode")!.Value);
        Assert.Null(placemark.Element(K + "TimeSpan"));
        Assert.Equal("#planned-starship", placemark.Element(K + "styleUrl")!.Value);
    }

    [Fact]
    public void PlannedPath_ExtendedData_ContainsQualityAndSource()
    {
        var path = DefaultPath();
        var xml = Text(PlannedPathGenerator.CreatePlacemark(Starship(path), path));

        Assert.Contains("name=\"DataQuality\"", xml);
        Assert.Contains("<value>Reconstructed</value>", xml);
        Assert.Contains("test source", xml);
        Assert.Contains("not live telemetry", xml);
    }

    [Fact]
    public void PathStyleId_EstimatedActual_NeverMeasuredStyle()
    {
        Assert.Equal("estimated-starship", KmlLod.PathStyleId(TrajectoryType.Actual, VehicleType.Starship, DataQuality.Estimated));
        Assert.Equal("estimated-superheavy", KmlLod.PathStyleId(TrajectoryType.Actual, VehicleType.SuperHeavy, DataQuality.Simulated));
        Assert.Equal("actual-starship", KmlLod.PathStyleId(TrajectoryType.Actual, VehicleType.Starship, DataQuality.Measured));
        Assert.Equal("planned-superheavy", KmlLod.PathStyleId(TrajectoryType.Planned, VehicleType.SuperHeavy, DataQuality.Estimated));
    }

    [Fact]
    public void GxTrack_WhenAndCoord_FromLaunchClockLonLatAlt()
    {
        var path = DefaultPath();
        var placemark = GxTrackGenerator.CreatePlacemark(Starship(path), path, Clock);
        var track = placemark.Element(Gx + "Track")!;

        var whens = track.Elements(K + "when").Select(e => e.Value).ToList();
        Assert.Equal(new[] { Clock.Format(0), Clock.Format(60), Clock.Format(120) }, whens);
        Assert.Equal("2026-12-01T23:30:00Z", whens[0]);
        Assert.Equal("2026-12-01T23:32:00Z", whens[2]);

        var coords = track.Elements(Gx + "coord").Select(e => e.Value).ToList();
        Assert.Equal(whens.Count, coords.Count);
        Assert.Equal("-97.157000 25.997000 10.3", coords[0]);
        Assert.Equal("absolute", track.Element(K + "altitudeMode")!.Value);
        Assert.Empty(track.Elements(Gx + "angles"));
    }

    [Fact]
    public void GxTrack_TimeSpan_FirstToLastPoint()
    {
        var path = DefaultPath();
        var span = GxTrackGenerator.CreatePlacemark(Starship(path), path, Clock).Element(K + "TimeSpan")!;

        Assert.Equal("2026-12-01T23:30:00Z", span.Element(K + "begin")!.Value);
        Assert.Equal("2026-12-01T23:32:00Z", span.Element(K + "end")!.Value);
    }

    [Fact]
    public void GxTrack_FractionalOffsets_UseMilliseconds()
    {
        var path = Path(Pt(0, 25.9, -97.1, 0), Pt(0.5, 25.91, -97.1, 5));
        var whens = GxTrackGenerator.CreatePlacemark(Starship(path), path, Clock)
            .Element(Gx + "Track")!.Elements(K + "when").Select(e => e.Value).ToList();

        Assert.Equal(new[] { "2026-12-01T23:30:00.000Z", "2026-12-01T23:30:00.500Z" }, whens);
    }

    [Fact]
    public void GxTrack_CompleteOrientation_AnglesOnlyOnModelTrack_AndDerivedNoteOnModelOnly()
    {
        var path = Path(Pt(0, 25.9, -97.1, 0, 90, 80, 0), Pt(10, 25.91, -97.0, 100, 91, 70, 5));
        var vehicle = Starship(path, "models/starship.dae");

        var model = VehicleGenerator.CreateModelPlacemark(vehicle, path, Clock);
        var angles = model.Element(Gx + "Track")!.Elements(Gx + "angles").Select(e => e.Value).ToList();
        Assert.Equal(new[] { "90.00 10.00 0.00", "91.00 20.00 5.00" }, angles);
        Assert.Contains("derived from the direction of travel", model.Element(K + "description")!.Value);

        // Trail and icon have no Model, so gx:angles would be ignored by Google Earth: not written.
        var trail = GxTrackGenerator.CreatePlacemark(vehicle, path, Clock);
        Assert.Empty(trail.Element(Gx + "Track")!.Elements(Gx + "angles"));
        var icon = VehicleGenerator.CreateIconPlacemark(vehicle, path, Clock);
        Assert.Empty(icon.Element(Gx + "Track")!.Elements(Gx + "angles"));
        Assert.DoesNotContain("derived from the direction of travel", icon.Element(K + "description")!.Value);
    }

    [Fact]
    public void GxTrack_IncompleteOrientation_NoAnglesAtAll_NoFakeZeros()
    {
        // Second point has no orientation, third only a heading: nothing is written, nothing is invented.
        var path = Path(Pt(0, 25.9, -97.1, 0, 90, 80, 0), Pt(10, 25.91, -97.0, 100), Pt(20, 25.92, -96.9, 200, heading: 90));
        var placemark = VehicleGenerator.CreateModelPlacemark(Starship(path, "models/starship.dae"), path, Clock);

        Assert.Empty(placemark.Element(Gx + "Track")!.Elements(Gx + "angles"));
        Assert.DoesNotContain("derived from the direction of travel", placemark.Element(K + "description")!.Value);
    }

    [Fact]
    public void Event_TimeStampPointAndQuality()
    {
        var ev = new MissionEvent(75, EventType.MaxQ, "Max Q", "Maximum dynamic pressure", 25.99, -97.1, 12000,
            VehicleType.Starship, DataQuality.Simulated, "sim source");
        var placemark = EventGenerator.CreatePlacemark(ev, Clock);

        Assert.Equal("2026-12-01T23:31:15Z", placemark.Element(K + "TimeStamp")!.Element(K + "when")!.Value);
        var point = placemark.Element(K + "Point")!;
        Assert.Equal("-97.100000,25.990000,12000.0", point.Element(K + "coordinates")!.Value);
        Assert.Equal("absolute", point.Element(K + "altitudeMode")!.Value);

        var description = placemark.Element(K + "description")!.Value;
        Assert.Contains("Data quality: Simulated", description);
        Assert.Contains("not measured telemetry", description);
        Assert.Contains("sim source", description);
        Assert.Contains("T+00:01:15", description);
        Assert.Contains("name=\"DataQuality\"", Text(placemark));
    }

    [Fact]
    public void Event_WithoutPosition_HasNoPoint_AndNoAltitude_ClampsToGround()
    {
        var noPos = new MissionEvent(-10, EventType.Liftoff, "Ignition", null, null, null, null,
            null, DataQuality.Published, "src");
        Assert.Null(EventGenerator.CreatePlacemark(noPos, Clock).Element(K + "Point"));

        var noAlt = new MissionEvent(0, EventType.Liftoff, "Liftoff", null, 25.99, -97.15, null,
            null, DataQuality.Published, "src");
        var point = EventGenerator.CreatePlacemark(noAlt, Clock).Element(K + "Point")!;
        Assert.Equal("clampToGround", point.Element(K + "altitudeMode")!.Value);
        Assert.Equal("-97.150000,25.990000", point.Element(K + "coordinates")!.Value);
    }

    [Fact]
    public void EventFolder_OrdersByOffset_AndNamed()
    {
        var late = new MissionEvent(100, EventType.Meco, "B", null, null, null, null, null, DataQuality.Estimated, "s");
        var early = new MissionEvent(10, EventType.MaxQ, "A", null, null, null, null, null, DataQuality.Estimated, "s");
        var folder = EventGenerator.CreateFolder([late, early], Clock);

        Assert.Equal("Mission Events", folder.Element(K + "name")!.Value);
        Assert.Equal(new[] { "A", "B" }, folder.Elements(K + "Placemark").Select(p => p.Element(K + "name")!.Value));
    }

    [Fact]
    public void Vehicle_ModelPlacemark_HasLinkScaleRegionLod_NoSecondOrientation()
    {
        var path = DefaultPath();
        var vehicle = Starship(path, "models/starship.dae");
        var placemark = VehicleGenerator.CreateModelPlacemark(vehicle, path, Clock);

        var model = placemark.Element(Gx + "Track")!.Element(K + "Model")!;
        Assert.Equal("files/starship.dae", model.Element(K + "Link")!.Element(K + "href")!.Value);
        Assert.Equal("1", model.Element(K + "Scale")!.Element(K + "x")!.Value);
        Assert.Null(model.Element(K + "Orientation"));

        var region = placemark.Element(K + "Region")!;
        var lod = region.Element(K + "Lod")!;
        Assert.Equal(KmlLod.ModelMinLodPixels.ToString(), lod.Element(K + "minLodPixels")!.Value);
        Assert.Equal("-1", lod.Element(K + "maxLodPixels")!.Value);
        var box = region.Element(K + "LatLonAltBox")!;
        Assert.Equal("26.500000", box.Element(K + "north")!.Value);
        Assert.Equal("-97.157000", box.Element(K + "west")!.Value);
    }

    [Fact]
    public void Vehicle_IconPlacemark_AlwaysVisible_AndSeparateFolders()
    {
        var path = DefaultPath();
        var vehicle = Starship(path);
        var icon = VehicleGenerator.CreateIconPlacemark(vehicle, path, Clock);

        Assert.Null(icon.Element(K + "Region"));
        Assert.Equal("#icon-starship", icon.Element(K + "styleUrl")!.Value);
        Assert.Null(icon.Element(Gx + "Track")!.Element(K + "Model"));

        var modelFolder = VehicleGenerator.CreateModelFolder(vehicle, path, Clock);
        var iconFolder = VehicleGenerator.CreateIconFolder(vehicle, path, Clock);
        Assert.Equal("Starship 3D Model", modelFolder.Element(K + "name")!.Value);
        Assert.Equal("Starship Icon", iconFolder.Element(K + "name")!.Value);
    }

    [Fact]
    public void Vehicle_ModelHref_UrlEscapesSpaces()
    {
        var path = DefaultPath();
        var vehicle = Starship(path, "files/Super Heavy v2.dae");
        var href = VehicleGenerator.CreateModelPlacemark(vehicle, path, Clock)
            .Element(Gx + "Track")!.Element(K + "Model")!.Element(K + "Link")!.Element(K + "href")!.Value;
        Assert.Equal("files/Super%20Heavy%20v2.dae", href);
    }

    [Fact]
    public void Vehicle_SinglePointPath_RegionIsNotZeroSize()
    {
        var path = Path(Pt(0, 25.997, -97.157, 10));
        var box = VehicleGenerator.CreateModelPlacemark(Starship(path), path, Clock)
            .Element(K + "Region")!.Element(K + "LatLonAltBox")!;

        double D(string n) => double.Parse(box.Element(K + n)!.Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(D("north") > D("south"));
        Assert.True(D("east") > D("west"));
        Assert.True(D("maxAltitude") > D("minAltitude"));
    }

    [Fact]
    public void Text_ControlCharactersAreStripped_SoXmlIsValid()
    {
        var ev = new MissionEvent(5, EventType.MaxQ, "Max\u0001Q\u000B", "bad\u0000 desc\uD800", null, null, null,
            null, DataQuality.Estimated, "src\u0008");
        var placemark = EventGenerator.CreatePlacemark(ev, Clock);

        Assert.Equal("MaxQ", placemark.Element(K + "name")!.Value);
        var xml = new XDocument(placemark).ToString();
        Assert.DoesNotContain(xml, c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
        Assert.Contains("bad desc", placemark.Element(K + "description")!.Value);
        _ = XDocument.Parse(xml); // parses back
    }

    [Fact]
    public void Vehicle_WithoutModelPath_ModelFolderIsEmpty()
    {
        var path = DefaultPath();
        var folder = VehicleGenerator.CreateModelFolder(Starship(path, model: null), path, Clock);
        Assert.Empty(folder.Elements(K + "Placemark"));
    }

    [Fact]
    public void Generators_SameInput_IdenticalOutput_AndGxNamespaceDeclared()
    {
        var path = DefaultPath();
        var vehicle = Starship(path);
        string Render() => Text(new XElement(K + "Document",
            new XAttribute(XNamespace.Xmlns + "gx", Gx.NamespaceName),
            PlannedPathGenerator.CreatePlacemark(vehicle, path),
            GxTrackGenerator.CreatePlacemark(vehicle, path, Clock),
            VehicleGenerator.CreateModelFolder(vehicle, path, Clock),
            VehicleGenerator.CreateIconFolder(vehicle, path, Clock)));

        var first = Render();
        Assert.Equal(first, Render());
        Assert.Contains("xmlns:gx=\"http://www.google.com/kml/ext/2.2\"", first);
        Assert.Contains("<gx:Track>", first);
        Assert.Contains("<gx:coord>", first);
        Assert.DoesNotContain(",", first.Split("<gx:coord>")[1].Split("</gx:coord>")[0]);
    }

    [Fact]
    public void Numbers_InvariantCulture_NoCommaDecimal()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("hu-HU");
            var path = DefaultPath();
            var coords = PlannedPathGenerator.CreatePlacemark(Starship(path), path)
                .Element(K + "LineString")!.Element(K + "coordinates")!.Value;
            Assert.StartsWith("-97.157000,25.997000,10.3 ", coords);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
