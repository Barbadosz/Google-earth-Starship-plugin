using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Kml.Tests.Generators;

/// <summary>N-20 package C/D: display lines (trail, path, curtain) are thinned; model and icon tracks stay full resolution.</summary>
public class DisplayLineThinningTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly XNamespace Gx = KmlXml.Gx;
    private static readonly DateTimeOffset T0 = new(2026, 12, 1, 14, 0, 0, TimeSpan.Zero);
    private static readonly LaunchClock Clock = new(T0);

    /// <summary>10 points, constant speed on a short straight line (about 1 km): thins to the two end points.</summary>
    private static FlightPath StraightPath(TrajectoryType type = TrajectoryType.Planned) =>
        new(type, DataQuality.Simulated, "test source", "MIT",
            Enumerable.Range(0, 10)
                .Select(i => new TrajectoryPoint(
                    i, 26.0, -97.0 + (i * 0.001), 1000 + (i * 10), null, DataQuality.Simulated, "test source", 90, 45, 0))
                .ToArray());

    private static Vehicle Starship(FlightPath path) => new("Starship", VehicleType.Starship, "models/starship.dae", [path]);

    private static string[] Tuples(XElement placemark) =>
        placemark.Element(K + "LineString")!.Element(K + "coordinates")!.Value.Split(' ');

    [Fact]
    public void PlannedPath_StraightLine_KeepsFirstAndLast_AndSaysSimplified()
    {
        var path = StraightPath();
        var placemark = PlannedPathGenerator.CreatePlacemark(Starship(path), path);

        Assert.Equal(["-97.000000,26.000000,1000.0", "-96.991000,26.000000,1090.0"], Tuples(placemark));
        var description = placemark.Element(K + "description")!.Value;
        Assert.Contains(KmlPathInfo.SimplifiedNote, description, StringComparison.Ordinal);
        Assert.DoesNotContain(KmlPathInfo.OrientationNote, description, StringComparison.Ordinal);

        // ExtendedData still reports the source point count.
        Assert.Contains("<value>10</value>", placemark.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }

    [Fact]
    public void PlannedPath_NothingDropped_NoSimplifiedNote()
    {
        var path = new FlightPath(TrajectoryType.Planned, DataQuality.Simulated, "test source", "MIT",
        [
            new TrajectoryPoint(0, 26.0, -97.0, 0, null, DataQuality.Simulated, "test source"),
            new TrajectoryPoint(10, 26.0, -96.9, 100, null, DataQuality.Simulated, "test source"),
            new TrajectoryPoint(20, 26.0, -96.8, 5000, null, DataQuality.Simulated, "test source"),
        ]);
        var placemark = PlannedPathGenerator.CreatePlacemark(Starship(path), path);

        Assert.Equal(3, Tuples(placemark).Length);
        Assert.DoesNotContain("simplified", placemark.Element(K + "description")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Curtain_UsesTheSameThinnedCoordinatesAsThePath()
    {
        var path = StraightPath();
        var line = PlannedPathGenerator.CreatePlacemark(Starship(path), path, extrude: false);
        var curtain = CurtainGenerator.CreatePlacemark(Starship(path), path);

        Assert.Equal(Tuples(line), Tuples(curtain));
        Assert.Equal(2, Tuples(curtain).Length);
    }

    [Fact]
    public void Trail_IsThinned_NoAngles_SpanUnchanged_AndSaysSimplified()
    {
        var path = StraightPath();
        var trail = GxTrackGenerator.CreatePlacemark(Starship(path), path, Clock);
        var track = trail.Element(Gx + "Track")!;

        Assert.Equal(2, track.Elements(Gx + "coord").Count());
        Assert.Equal(["2026-12-01T14:00:00Z", "2026-12-01T14:00:09Z"], track.Elements(K + "when").Select(w => w.Value));
        Assert.Empty(track.Elements(Gx + "angles"));
        var span = trail.Element(K + "TimeSpan")!;
        Assert.Equal("2026-12-01T14:00:00Z", span.Element(K + "begin")!.Value);
        Assert.Equal("2026-12-01T14:00:09Z", span.Element(K + "end")!.Value);
        var description = trail.Element(K + "description")!.Value;
        Assert.Contains(KmlPathInfo.SimplifiedNote, description, StringComparison.Ordinal);
        Assert.DoesNotContain(KmlPathInfo.OrientationNote, description, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelAndIcon_StayFullResolution()
    {
        var path = StraightPath();
        var vehicle = Starship(path);

        var model = VehicleGenerator.CreateModelPlacemark(vehicle, path, Clock).Element(Gx + "Track")!;
        Assert.Equal(10, model.Elements(Gx + "coord").Count());
        Assert.Equal(10, model.Elements(Gx + "angles").Count());
        var icon = VehicleGenerator.CreateIconPlacemark(vehicle, path, Clock).Element(Gx + "Track")!;
        Assert.Equal(10, icon.Elements(Gx + "coord").Count());
    }

    [Fact]
    public void MissionKmlBuilder_ThinsEachPathOnce_ReportsCounts_AndKeepsVehicleTracksFull()
    {
        var path = StraightPath();
        var mission = new Mission(
            new MissionConfig("test-mission", "Test mission", T0, "Illustrative test data.", []),
            Clock,
            [Starship(path)]);
        var reports = new List<MissionKmlBuilder.ThinnedPath>();

        var doc = XDocument.Parse(new MissionKmlBuilder(onPathThinned: reports.Add).Build(mission));

        var report = Assert.Single(reports);
        Assert.Equal(new MissionKmlBuilder.ThinnedPath("Starship", "test source", TrajectoryType.Planned, 10, 2), report);
        Assert.All(doc.Descendants(K + "LineString"), ls => Assert.Equal(2, ls.Element(K + "coordinates")!.Value.Split(' ').Length));
        Assert.Equal(2, doc.Descendants(K + "LineString").Count()); // path + curtain

        var tracks = doc.Descendants(K + "Placemark")
            .ToDictionary(p => p.Element(K + "name")!.Value, p => p.Element(Gx + "Track"));
        Assert.Equal(2, tracks["Starship – planned track"]!.Elements(Gx + "coord").Count());
        Assert.Equal(10, tracks["Starship 3D Model"]!.Elements(Gx + "coord").Count());
        Assert.Equal(10, tracks["Starship Icon"]!.Elements(Gx + "coord").Count());
    }
}
