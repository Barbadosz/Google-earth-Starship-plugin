using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Kml.Styles;

namespace StarshipMissionTracker.Kml.Tests.Styles;

public class KmlToggleStylesTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly LaunchClock Clock = new(new DateTimeOffset(2026, 12, 1, 14, 0, 0, TimeSpan.Zero));

    private static MissionEvent Event(double offset, double? lat = 25.9, double? lon = -97.1) =>
        new(offset, EventType.Liftoff, "Liftoff", "desc", lat, lon, 100, VehicleType.Starship, DataQuality.Published, "src");

    [Fact]
    public void CreateToggleStyles_IdsUniqueAndDisjointFromCreateAll()
    {
        var toggle = KmlStyles.CreateToggleStyles().Select(s => s.Attribute("id")!.Value).ToList();
        var all = KmlStyles.CreateAll().Select(s => s.Attribute("id")!.Value);

        Assert.Equal(toggle.Count, toggle.Distinct().Count());
        Assert.Empty(toggle.Intersect(all));
    }

    [Fact]
    public void CreateToggleStyles_DefinesLabelFolderAndPlannedReconstructedCurtainStyles()
    {
        var ids = KmlStyles.CreateToggleStyles().Select(s => s.Attribute("id")!.Value).ToHashSet();

        Assert.Contains(KmlLod.ToggleStyleId.EventLabel, ids);
        Assert.Contains(KmlLod.ToggleStyleId.ToggleFolder, ids);
        Assert.Contains(KmlLod.CurtainStyleId(TrajectoryType.Planned, VehicleType.Starship, DataQuality.Published), ids);
        Assert.Contains(KmlLod.CurtainStyleId(TrajectoryType.Reconstructed, VehicleType.SuperHeavy, DataQuality.Published), ids);
        Assert.DoesNotContain(KmlLod.CurtainStyleId(TrajectoryType.Actual, VehicleType.SuperHeavy, DataQuality.Estimated), ids);
    }

    [Fact]
    public void EventLabelStyle_HidesIconKeepsLabel()
    {
        var style = KmlStyles.CreateToggleStyles().Single(s => (string)s.Attribute("id")! == KmlLod.ToggleStyleId.EventLabel);

        Assert.Equal("0", (string)style.Element(K + "IconStyle")!.Element(K + "scale")!);
        Assert.Equal("0.8", (string)style.Element(K + "LabelStyle")!.Element(K + "scale")!);
    }

    [Fact]
    public void ToggleFolderStyle_IsCheckHideChildren()
    {
        var style = KmlStyles.CreateToggleStyles().Single(s => (string)s.Attribute("id")! == KmlLod.ToggleStyleId.ToggleFolder);

        Assert.Equal("checkHideChildren", (string)style.Element(K + "ListStyle")!.Element(K + "listItemType")!);
    }

    [Fact]
    public void CurtainStyle_HasInvisibleLineAndPlannedPolyColour()
    {
        var id = KmlLod.CurtainStyleId(TrajectoryType.Planned, VehicleType.Starship, DataQuality.Published);
        var style = KmlStyles.CreateToggleStyles().Single(s => (string)s.Attribute("id")! == id);

        Assert.Equal("0", (string)style.Element(K + "LineStyle")!.Element(K + "width")!);
        Assert.Equal(KmlStyles.CurtainAlpha + KmlStyles.PlannedColor[2..], (string)style.Element(K + "PolyStyle")!.Element(K + "color")!);
    }

    [Fact]
    public void CreateAll_IsUnchangedByToggleStyles()
    {
        Assert.DoesNotContain(KmlStyles.CreateAll(), s => (string)s.Attribute("id")! == KmlLod.ToggleStyleId.EventLabel);
    }

    [Fact]
    public void LabelFolder_HasIdStyleAndSkipsEventsWithoutCoordinates()
    {
        var folder = EventPlacemarkBuilder.CreateLabelFolder([Event(10), Event(5, null, null)], Clock, EventPlacemarkOptions.Kmz);

        Assert.Equal(KmlLod.FolderId.EventLabels, (string)folder.Attribute("id")!);
        Assert.Equal("#toggle-folder", (string)folder.Element(K + "styleUrl")!);
        var placemark = Assert.Single(folder.Elements(K + "Placemark"));
        Assert.Equal("T+00:00:10 Liftoff", (string)placemark.Element(K + "name")!);
        Assert.Equal("#event-label", (string)placemark.Element(K + "styleUrl")!);
        Assert.Null(placemark.Element(K + "description"));
        Assert.NotNull(placemark.Element(K + "TimeStamp"));
    }

    [Fact]
    public void LabelPlacemark_SharesCoordinatesWithEventPlacemark_AndDynamicHasNoTimeStamp()
    {
        var e = Event(30);
        var label = EventPlacemarkBuilder.CreateLabelPlacemark(e, Clock, EventPlacemarkOptions.Dynamic);
        var icon = EventPlacemarkBuilder.CreatePlacemark(e, Clock, EventPlacemarkOptions.Dynamic);

        Assert.Equal((string)icon.Element(K + "Point")!.Element(K + "coordinates")!, (string)label.Element(K + "Point")!.Element(K + "coordinates")!);
        Assert.Null(label.Element(K + "TimeStamp"));
    }
}
