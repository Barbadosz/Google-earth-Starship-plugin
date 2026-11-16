using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Kml.Tests.Generators;

public class PlannedPathExtrudeTests
{
    private static readonly XNamespace K = KmlXml.Kml;

    private static readonly FlightPath Path = new(
        TrajectoryType.Planned, DataQuality.Reconstructed, "test source", "CC0",
        [new TrajectoryPoint(0, 25.9, -97.1, 10, null, DataQuality.Reconstructed, "s", null, null, null),
         new TrajectoryPoint(60, 26.1, -96.5, 5000, null, DataQuality.Reconstructed, "s", null, null, null)]);

    private static readonly Vehicle Ship = new("Starship", VehicleType.Starship, null, [Path]);

    [Fact]
    public void Default_IsByteIdenticalToExtrudeTrue()
    {
        var def = PlannedPathGenerator.CreatePlacemark(Ship, Path).ToString(SaveOptions.DisableFormatting);
        var explicitTrue = PlannedPathGenerator.CreatePlacemark(Ship, Path, extrude: true).ToString(SaveOptions.DisableFormatting);

        Assert.Equal(def, explicitTrue);
        Assert.Contains("<extrude>1</extrude>", def);
    }

    [Fact]
    public void ExtrudeFalse_OnlyExtrudeValueDiffers()
    {
        var on = PlannedPathGenerator.CreatePlacemark(Ship, Path, extrude: true).ToString(SaveOptions.DisableFormatting);
        var off = PlannedPathGenerator.CreatePlacemark(Ship, Path, extrude: false).ToString(SaveOptions.DisableFormatting);

        Assert.Contains("<extrude>0</extrude>", off);
        Assert.Equal(on.Replace("<extrude>1</extrude>", "<extrude>0</extrude>"), off);
    }
}
