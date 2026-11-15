using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Kml.Styles;

namespace StarshipMissionTracker.Kml.Tests.Styles;

public class KmlStylesIconBaseTests
{
    [Fact]
    public void CreateAllWithBase_SameIdsAndOrder_OnlyVehicleIconHrefsChange()
    {
        var plain = KmlStyles.CreateAll();
        var served = KmlStyles.CreateAll("http://127.0.0.1:8080/assets/icons/");

        Assert.Equal(plain.Count, served.Count);
        Assert.Equal(
            plain.Select(s => s.Attribute("id")!.Value),
            served.Select(s => s.Attribute("id")!.Value));

        var hrefs = served.Select(s => s.Descendants(KmlXml.Kml + "href").Select(h => h.Value).FirstOrDefault()).ToList();
        Assert.Contains("http://127.0.0.1:8080/assets/icons/starship.png", hrefs);
        Assert.Contains("http://127.0.0.1:8080/assets/icons/booster.png", hrefs);
        Assert.DoesNotContain(KmlStyles.StarshipIconHref, hrefs);

        // Everything that is not a vehicle icon style is identical to the parameterless overload.
        for (var i = 0; i < plain.Count; i++)
        {
            var id = plain[i].Attribute("id")!.Value;
            if (id != KmlLod.StyleId.IconStarship && id != KmlLod.StyleId.IconSuperHeavy)
            {
                Assert.Equal(plain[i].ToString(), served[i].ToString());
            }
        }
    }

    [Fact]
    public void CreateAllWithBase_NoTrailingSlash_GivesSameResult() =>
        Assert.Equal(
            string.Concat(KmlStyles.CreateAll("http://h/i/")),
            string.Concat(KmlStyles.CreateAll("http://h/i")));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateAllWithBase_Blank_Throws(string baseHref) =>
        Assert.Throws<ArgumentException>(() => KmlStyles.CreateAll(baseHref));
}
