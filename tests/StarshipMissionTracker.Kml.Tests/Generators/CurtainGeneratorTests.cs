using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Kml.Tests.Generators;

public class CurtainGeneratorTests
{
    private static readonly XNamespace K = KmlXml.Kml;

    private static TrajectoryPoint Pt(double t, double lat, double lon, double alt) =>
        new(t, lat, lon, alt, null, DataQuality.Reconstructed, "test source", null, null, null);

    private static FlightPath Path(params TrajectoryPoint[] points) =>
        new(TrajectoryType.Planned, DataQuality.Reconstructed, "test source", "CC0", points);

    private static Vehicle Starship(FlightPath path) => new("Starship", VehicleType.Starship, null, [path]);

    private static FlightPath DefaultPath() => Path(
        Pt(0, 25.997, -97.157, 10.25),
        Pt(60, 26.1, -96.5, 12345.67),
        Pt(120, 26.5, -95.0, 50000));

    [Fact]
    public void Folder_HasIdNameAndToggleStyle()
    {
        var path = DefaultPath();
        var folder = CurtainGenerator.CreateFolder([(Starship(path), path)]);

        Assert.Equal(KmlLod.FolderId.Curtain, (string?)folder.Attribute("id"));
        Assert.Equal(KmlLod.FolderName.FlightCurtain, folder.Element(K + "name")!.Value);
        Assert.Equal("#toggle-folder", folder.Element(K + "styleUrl")!.Value);
        Assert.Single(folder.Elements(K + "Placemark"));
    }

    [Fact]
    public void Placemark_ExtrudedWithCurtainStyle_SameCoordinatesAsPath()
    {
        var path = DefaultPath();
        var vehicle = Starship(path);
        var curtain = CurtainGenerator.CreatePlacemark(vehicle, path);
        var line = curtain.Element(K + "LineString")!;

        Assert.Equal("1", line.Element(K + "extrude")!.Value);
        Assert.Equal("absolute", line.Element(K + "altitudeMode")!.Value);
        Assert.Equal("#" + KmlLod.CurtainStyleId(path.Type, vehicle.Type, path.Quality), curtain.Element(K + "styleUrl")!.Value);
        Assert.StartsWith("#curtain-", curtain.Element(K + "styleUrl")!.Value);

        var pathLine = PlannedPathGenerator.CreatePlacemark(vehicle, path).Element(K + "LineString")!;
        Assert.Equal(pathLine.Element(K + "coordinates")!.Value, line.Element(K + "coordinates")!.Value);
    }

    [Fact]
    public void Folder_SkipsEmptyPaths()
    {
        var empty = Path();
        var folder = CurtainGenerator.CreateFolder([(Starship(empty), empty)]);

        Assert.Empty(folder.Elements(K + "Placemark"));
    }

    [Fact]
    public void Placemark_EmptyPath_Throws()
    {
        var empty = Path();
        Assert.Throws<ArgumentException>(() => CurtainGenerator.CreatePlacemark(Starship(empty), empty));
    }
}
