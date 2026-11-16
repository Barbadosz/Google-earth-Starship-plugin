using System.Globalization;
using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Kml.Tests;

public class MissionKmlBuilderTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly XNamespace Gx = KmlXml.Gx;

    private static readonly DateTimeOffset T0 = new(2026, 12, 1, 14, 0, 0, TimeSpan.Zero);

    private static TrajectoryPoint Pt(double t, double lat, double lon, double alt) =>
        new(t, lat, lon, alt, null, DataQuality.Simulated, "test source", 90, 45, 0);

    private static Mission CreateMission(DateTimeOffset t0)
    {
        var starship = new FlightPath(TrajectoryType.Planned, DataQuality.Simulated, "test source", "MIT",
        [
            Pt(0, 25.9972, -97.1566, 5),
            Pt(60, 26.0, -97.0, 12000),
            Pt(120.5, 26.0, -96.0, 60000),
        ]);
        var booster = new FlightPath(TrajectoryType.Planned, DataQuality.Simulated, "test source", "MIT",
        [
            Pt(0, 25.9972, -97.1566, 5),
            Pt(60, 26.0, -97.0, 12000),
            Pt(400, 25.9972, -97.1566, 5),
        ]);
        var events = new List<MissionEvent>
        {
            new(0, EventType.Liftoff, "Liftoff", "", 25.9972, -97.1566, 5, null, DataQuality.Simulated, "test source"),
            new(160, EventType.StageSeparation, "Stage Separation", "", 26.0, -96.2, 68000, null, DataQuality.Simulated, "test source"),
            new(520, EventType.Coast, "Coast", "", null, null, null, VehicleType.Starship, DataQuality.Simulated, "test source"),
        };
        var config = new MissionConfig("test-mission", "Test mission", t0, "Illustrative test data.", events);
        return new Mission(config, new LaunchClock(t0),
        [
            new Vehicle("Starship", VehicleType.Starship, "models/starship.dae", [starship]),
            new Vehicle("Super Heavy", VehicleType.SuperHeavy, "models/superheavy.dae", [booster]),
        ]);
    }

    private static XDocument Parse(string kml) => XDocument.Parse(kml);

    private static XElement Document(XDocument doc) => doc.Root!.Element(K + "Document")!;

    private static XElement ChildFolder(XElement parent, string name) =>
        parent.Elements(K + "Folder").Single(f => f.Element(K + "name")?.Value == name);

    [Fact]
    public void Build_WellFormedKml_DeclaresDefaultAndGxNamespace()
    {
        var kml = new MissionKmlBuilder().Build(CreateMission(T0));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", kml, StringComparison.OrdinalIgnoreCase);
        var doc = Parse(kml);
        Assert.Equal(K + "kml", doc.Root!.Name);
        Assert.Equal(Gx.NamespaceName, doc.Root.Attribute(XNamespace.Xmlns + "gx")!.Value);
        Assert.Equal(K.NamespaceName, doc.Root.GetDefaultNamespace().NamespaceName);
        Assert.DoesNotContain("\r", kml);
    }

    [Fact]
    public void Build_AllStyleUrls_AreDefined()
    {
        var doc = Parse(new MissionKmlBuilder().Build(CreateMission(T0)));
        var defined = doc.Descendants(K + "Style").Select(s => "#" + s.Attribute("id")!.Value).ToHashSet();
        var used = doc.Descendants(K + "styleUrl").Select(s => s.Value).Distinct().ToList();

        Assert.NotEmpty(used);
        Assert.All(used, url => Assert.Contains(url, defined));
    }

    [Fact]
    public void Build_FolderHierarchy_PerSpec_ForBothVehicles()
    {
        var document = Document(Parse(new MissionKmlBuilder().Build(CreateMission(T0))));

        var topNames = document.Elements(K + "Folder").Select(f => f.Element(K + "name")!.Value).ToList();
        Assert.Equal(
            [
                KmlLod.FolderName.MissionInformation, KmlLod.FolderName.PlannedTrajectory, KmlLod.FolderName.FlightCurtain,
                KmlLod.FolderName.ActualTrajectory, KmlLod.FolderName.Vehicles, KmlLod.FolderName.MissionEvents,
                KmlLod.FolderName.EventLabels,
            ],
            topNames);

        var planned = ChildFolder(document, KmlLod.FolderName.PlannedTrajectory);
        var vehicles = ChildFolder(document, KmlLod.FolderName.Vehicles);
        foreach (var (folderName, vehicleName) in new[]
                 { (KmlLod.FolderName.Starship, "Starship"), (KmlLod.FolderName.SuperHeavy, "Super Heavy") })
        {
            var pathFolder = ChildFolder(planned, folderName);
            var placemarks = pathFolder.Elements(K + "Placemark").ToList();
            Assert.Contains(placemarks, p => p.Element(K + "LineString") is not null);
            Assert.Contains(placemarks, p => p.Element(Gx + "Track") is not null);

            var vehicleFolder = ChildFolder(vehicles, folderName);
            var model = ChildFolder(vehicleFolder, $"{vehicleName} 3D Model");
            var icon = ChildFolder(vehicleFolder, $"{vehicleName} Icon");
            Assert.NotNull(model.Descendants(K + "Model").SingleOrDefault());
            Assert.Single(icon.Elements(K + "Placemark"));
        }

        Assert.Equal(3, ChildFolder(document, KmlLod.FolderName.MissionEvents).Elements(K + "Placemark").Count());
    }

    // ---- N-39a toggle layers / N-40a LookAt ----

    [Fact]
    public void Build_ToggleFolders_HaveStableIds_AndSingleCheckboxStyle()
    {
        var document = Document(Parse(new MissionKmlBuilder().Build(CreateMission(T0))));
        var styles = document.Elements(K + "Style").ToDictionary(s => s.Attribute("id")!.Value);

        var curtain = ChildFolder(document, KmlLod.FolderName.FlightCurtain);
        var labels = ChildFolder(document, KmlLod.FolderName.EventLabels);
        Assert.Equal(KmlLod.FolderId.Curtain, curtain.Attribute("id")!.Value);
        Assert.Equal(KmlLod.FolderId.EventLabels, labels.Attribute("id")!.Value);
        Assert.All(new[] { curtain, labels }, f => Assert.Equal("#" + KmlLod.ToggleStyleId.ToggleFolder, f.Element(K + "styleUrl")!.Value));
        Assert.Equal("checkHideChildren",
            styles[KmlLod.ToggleStyleId.ToggleFolder].Descendants(K + "listItemType").Single().Value);
        Assert.Null(ChildFolder(document, KmlLod.FolderName.MissionEvents).Attribute("id"));
        Assert.Equal("0", styles[KmlLod.StyleId.Event].Element(K + "LabelStyle")!.Element(K + "scale")!.Value);
        Assert.Equal("0.8", styles[KmlLod.ToggleStyleId.EventLabel].Element(K + "LabelStyle")!.Element(K + "scale")!.Value);
    }

    [Fact]
    public void Build_Curtain_HasThePlannedPathCoordinates_PathLineIsNotExtruded_NoDensificationNote()
    {
        var mission = CreateMission(T0);
        var document = Document(Parse(new MissionKmlBuilder(TrajectoryDensificationOptions.Default, AllPaths(mission)).Build(mission)));
        var planned = ChildFolder(document, KmlLod.FolderName.PlannedTrajectory);
        var curtain = ChildFolder(document, KmlLod.FolderName.FlightCurtain);

        var pathLines = planned.Descendants(K + "LineString").ToList();
        var curtainLines = curtain.Descendants(K + "LineString").ToList();
        Assert.Equal(2, pathLines.Count);
        Assert.All(pathLines, ls => Assert.Equal("0", ls.Element(K + "extrude")!.Value));
        Assert.All(curtainLines, ls => Assert.Equal("1", ls.Element(K + "extrude")!.Value));
        Assert.Equal(
            pathLines.Select(ls => ls.Element(K + "coordinates")!.Value),
            curtainLines.Select(ls => ls.Element(K + "coordinates")!.Value));
        Assert.All(curtain.Elements(K + "Placemark"), p => Assert.Null(p.Element(K + "description")));
        Assert.All(curtain.Elements(K + "Placemark"), p => Assert.StartsWith("#" + KmlLod.ToggleStyleId.CurtainPrefix, p.Element(K + "styleUrl")!.Value));
    }

    [Fact]
    public void Build_EventLabels_ShareIconCoordinates_AndTimeStamps()
    {
        var document = Document(Parse(new MissionKmlBuilder().Build(CreateMission(T0))));
        var icons = ChildFolder(document, KmlLod.FolderName.MissionEvents).Elements(K + "Placemark")
            .Where(p => p.Element(K + "Point") is not null).ToList();
        var labels = ChildFolder(document, KmlLod.FolderName.EventLabels).Elements(K + "Placemark").ToList();

        // The Coast event has no position: it has an icon placemark but no label.
        Assert.Equal(icons.Count, labels.Count);
        for (var i = 0; i < icons.Count; i++)
        {
            Assert.EndsWith(icons[i].Element(K + "name")!.Value, labels[i].Element(K + "name")!.Value, StringComparison.Ordinal);
            Assert.StartsWith("T+", labels[i].Element(K + "name")!.Value, StringComparison.Ordinal);
            Assert.Equal(PointCoordinates(icons[i]), PointCoordinates(labels[i]));
            Assert.Equal(icons[i].Element(K + "TimeStamp")!.Value, labels[i].Element(K + "TimeStamp")!.Value);
            Assert.Null(labels[i].Element(K + "description"));
        }

        static string PointCoordinates(XElement p) => p.Element(K + "Point")!.Element(K + "coordinates")!.Value;
    }

    [Fact]
    public void Build_MissionInformation_HasTimelineTable_AndLookAtAtT0()
    {
        var document = Document(Parse(new MissionKmlBuilder().Build(CreateMission(T0))));
        var placemark = ChildFolder(document, KmlLod.FolderName.MissionInformation).Element(K + "Placemark")!;
        var description = placemark.Element(K + "description")!.Value;

        Assert.Contains(TimelineHtml.Heading, description, StringComparison.Ordinal);
        Assert.Contains("T+00:02:40", description, StringComparison.Ordinal);
        Assert.Contains("Stage Separation", description, StringComparison.Ordinal);
        Assert.Equal(["name", "description", "LookAt", "Point"], placemark.Elements().Select(e => e.Name.LocalName).Take(4));
        var lookAt = placemark.Element(K + "LookAt")!;
        Assert.Equal("2026-12-01T14:00:00Z", lookAt.Element(Gx + "TimeStamp")!.Element(K + "when")!.Value);
        Assert.Equal(document.Element(K + "LookAt")!.ToString(), lookAt.ToString());
    }

    [Fact]
    public void Build_LookAt_OverLaunchSite_AtT0()
    {
        var document = Document(Parse(new MissionKmlBuilder().Build(CreateMission(T0))));
        var lookAt = document.Element(K + "LookAt")!;

        Assert.Equal("-97.156600", lookAt.Element(K + "longitude")!.Value);
        Assert.Equal("25.997200", lookAt.Element(K + "latitude")!.Value);
        Assert.Equal("2026-12-01T14:00:00Z", lookAt.Element(Gx + "TimeStamp")!.Element(K + "when")!.Value);
    }

    [Fact]
    public void Build_Description_ShowsT0AndIllustrativeNote()
    {
        var document = Document(Parse(new MissionKmlBuilder().Build(CreateMission(T0))));
        var description = document.Element(K + "description")!.Value;

        Assert.Contains("2026-12-01T14:00:00Z", description);
        Assert.Contains("subject to change", description);
        Assert.Contains("Simulated", description);
        Assert.Contains("not live telemetry", description);
    }

    [Fact]
    public void Build_ChangingT0_ShiftsEveryTimeValueExactly()
    {
        var later = T0.AddHours(5).AddMinutes(17).AddSeconds(3);
        var a = Parse(new MissionKmlBuilder().Build(CreateMission(T0)));
        var b = Parse(new MissionKmlBuilder().Build(CreateMission(later)));

        static List<DateTimeOffset> Times(XDocument d) =>
            d.Descendants()
                .Where(e => e.Name.LocalName is "when" or "begin" or "end")
                .Select(e => DateTimeOffset.Parse(e.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
                .ToList();

        var before = Times(a);
        var after = Times(b);
        Assert.NotEmpty(before);
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(later - T0, after[i] - before[i]);
        }
    }

    [Fact]
    public void Build_TrackWhen_IsT0PlusOffset()
    {
        var doc = Parse(new MissionKmlBuilder().Build(CreateMission(T0)));
        var track = doc.Descendants(Gx + "Track").First();
        var whens = track.Elements(K + "when").Select(w => w.Value).ToList();

        // One offset (120.5) is fractional, so the whole path uses milliseconds.
        Assert.Equal(["2026-12-01T14:00:00.000Z", "2026-12-01T14:01:00.000Z", "2026-12-01T14:02:00.500Z"], whens);
    }

    [Fact]
    public void Build_EventTimeStamps_AreT0PlusOffset()
    {
        var mission = CreateMission(T0);
        var document = Document(Parse(new MissionKmlBuilder().Build(mission)));
        var events = ChildFolder(document, KmlLod.FolderName.MissionEvents).Elements(K + "Placemark").ToList();

        foreach (var e in mission.Config.Events)
        {
            var placemark = events.Single(p => p.Element(K + "name")!.Value == e.Name);
            var when = placemark.Element(K + "TimeStamp")!.Element(K + "when")!.Value;
            Assert.Equal(T0.AddSeconds(e.OffsetSeconds).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), when);
        }
    }

    private static HashSet<FlightPath> AllPaths(Mission mission) =>
        new(mission.Vehicles.SelectMany(v => v.FlightPaths), ReferenceEqualityComparer.Instance);

    [Fact]
    public void Build_DensificationOptionsButNoInsertedPoints_NoNote()
    {
        var mission = CreateMission(T0);
        var kml = new MissionKmlBuilder(TrajectoryDensificationOptions.Default, new HashSet<FlightPath>(ReferenceEqualityComparer.Instance))
            .Build(mission);

        Assert.DoesNotContain("Densified", kml);
        Assert.DoesNotContain("downgraded", kml);
    }

    [Fact]
    public void Build_OnlyDensifiedPathsGetTheNote()
    {
        var mission = CreateMission(T0);
        var starshipPath = mission.Vehicles.Single(v => v.Type == VehicleType.Starship).FlightPaths[0];
        var set = new HashSet<FlightPath>([starshipPath], ReferenceEqualityComparer.Instance);

        var doc = Parse(new MissionKmlBuilder(new TrajectoryDensificationOptions(10000, 10), set).Build(mission));
        var planned = ChildFolder(Document(doc), KmlLod.FolderName.PlannedTrajectory);

        var starship = ChildFolder(planned, KmlLod.FolderName.Starship).Elements(K + "Placemark").ToList();
        var booster = ChildFolder(planned, KmlLod.FolderName.SuperHeavy).Elements(K + "Placemark").ToList();
        Assert.All(starship, p => Assert.Contains("Densified", p.Element(K + "description")!.Value));
        Assert.All(booster, p => Assert.DoesNotContain("Densified", p.Element(K + "description")!.Value));
    }

    [Fact]
    public void Build_IsCompact_NoIndentation()
    {
        var kml = new MissionKmlBuilder().Build(CreateMission(T0));

        Assert.DoesNotContain("\n  <", kml);
        Assert.DoesNotContain("\r", kml);
    }

    [Fact]
    public void Build_WithDensification_MentionsItInPathDescriptions()
    {
        var options = new TrajectoryDensificationOptions(10000, 10);
        var mission = CreateMission(T0);
        var doc = Parse(new MissionKmlBuilder(options, AllPaths(mission)).Build(mission));
        var planned = ChildFolder(Document(doc), KmlLod.FolderName.PlannedTrajectory);
        var descriptions = planned.Descendants(K + "Placemark").Select(p => p.Element(K + "description")!.Value).ToList();

        Assert.Equal(4, descriptions.Count);
        Assert.All(descriptions, d => Assert.Contains("Densified", d));
        Assert.All(descriptions, d => Assert.Contains("10 km or 10 s", d));
        Assert.All(descriptions, d => Assert.Contains("Estimated", d));
    }

    [Fact]
    public void Build_WithoutDensification_NoDensificationNote()
    {
        var kml = new MissionKmlBuilder().Build(CreateMission(T0));
        Assert.DoesNotContain("Densified", kml);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var builder = new MissionKmlBuilder(TrajectoryDensificationOptions.Default);
        Assert.Equal(builder.Build(CreateMission(T0)), builder.Build(CreateMission(T0)));
    }

    [Fact]
    public void EventIcon_UsesHttps()
    {
        Assert.StartsWith("https://", StarshipMissionTracker.Kml.Styles.KmlStyles.EventIconHref, StringComparison.Ordinal);
    }

    [Fact]
    public void FollowedPath_PrefersActual()
    {
        var planned = new FlightPath(TrajectoryType.Planned, DataQuality.Simulated, "s", null, [Pt(0, 0, 0, 0)]);
        var actual = new FlightPath(TrajectoryType.Actual, DataQuality.Simulated, "s", null, [Pt(0, 1, 1, 0)]);
        var vehicle = new Vehicle("Starship", VehicleType.Starship, null, [planned, actual]);

        Assert.Same(actual, MissionKmlBuilder.FollowedPath(vehicle));
    }
}
