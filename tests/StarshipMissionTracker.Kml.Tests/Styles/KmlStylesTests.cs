using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Kml.Styles;

namespace StarshipMissionTracker.Kml.Tests.Styles;

public class KmlStylesTests
{
    private static readonly XNamespace K = KmlXml.Kml;

    private static readonly string[] PathStyleIds =
    [
        KmlLod.StyleId.PlannedStarship, KmlLod.StyleId.PlannedSuperHeavy,
        KmlLod.StyleId.ReconstructedStarship, KmlLod.StyleId.ReconstructedSuperHeavy,
        KmlLod.StyleId.ActualStarship, KmlLod.StyleId.ActualSuperHeavy,
        KmlLod.StyleId.EstimatedStarship, KmlLod.StyleId.EstimatedSuperHeavy,
    ];

    private static Dictionary<string, XElement> StylesById() =>
        KmlStyles.CreateAll().ToDictionary(s => s.Attribute("id")!.Value);

    [Fact]
    public void CreateAll_DefinesEveryStyleIdOfKmlLod()
    {
        var ids = StylesById().Keys.ToHashSet();
        var expected = typeof(KmlLod.StyleId).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.All(expected, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void CreateAll_IdsAreUnique()
    {
        var all = KmlStyles.CreateAll().Select(s => s.Attribute("id")!.Value).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PathStyles_HideTrackIcon_WithIconScaleZero()
    {
        var styles = StylesById();
        Assert.All(PathStyleIds, id =>
            Assert.Equal("0", styles[id].Element(K + "IconStyle")!.Element(K + "scale")!.Value));
    }

    [Fact]
    public void PlannedStyles_AreBlue_EstimatedOrange_ActualGreen()
    {
        var styles = StylesById();
        string Line(string id) => styles[id].Element(K + "LineStyle")!.Element(K + "color")!.Value;

        // aabbggrr: blue = full blue channel, no red.
        Assert.Equal("ffff0000", Line(KmlLod.StyleId.PlannedStarship));
        Assert.StartsWith("ffff", Line(KmlLod.StyleId.PlannedSuperHeavy));
        Assert.EndsWith("00", Line(KmlLod.StyleId.PlannedSuperHeavy));
        Assert.Equal("ff0080ff", Line(KmlLod.StyleId.EstimatedStarship));
        Assert.Equal("ff0080ff", Line(KmlLod.StyleId.EstimatedSuperHeavy));
        Assert.Equal("ff00ff00", Line(KmlLod.StyleId.ActualStarship));
    }

    [Fact]
    public void EstimatedActualPath_NeverGetsMeasuredGreen()
    {
        var id = KmlLod.PathStyleId(TrajectoryType.Actual, VehicleType.Starship, DataQuality.Estimated);
        Assert.NotEqual(KmlStyles.ActualColor, KmlStyles.PathColor(id));
    }

    [Fact]
    public void IconStyles_ReferenceKmzIconFiles()
    {
        var styles = StylesById();
        string Href(string id) =>
            styles[id].Element(K + "IconStyle")!.Element(K + "Icon")!.Element(K + "href")!.Value;

        Assert.Equal("files/icons/starship.png", Href(KmlLod.StyleId.IconStarship));
        Assert.Equal("files/icons/booster.png", Href(KmlLod.StyleId.IconSuperHeavy));
        Assert.Equal(KmlStyles.IconHrefs.Order(), new[] { Href(KmlLod.StyleId.IconStarship), Href(KmlLod.StyleId.IconSuperHeavy) }.Order());
        Assert.Equal(KmlStyles.StarshipIconHref, KmlStyles.IconHref(VehicleType.Starship));
    }
}
