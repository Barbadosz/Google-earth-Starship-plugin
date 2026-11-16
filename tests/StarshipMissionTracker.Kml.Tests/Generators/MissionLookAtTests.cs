using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Kml.Tests.Generators;

public class MissionLookAtTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly XNamespace Gx = KmlXml.Gx;
    private static readonly LaunchClock Clock = new(new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.Zero));

    [Fact]
    public void WithClock_HasTimeStampAtT0_FirstChild_AndFormerValues()
    {
        var lookAt = MissionLookAt.Create(25.997, -97.155, Clock);

        Assert.Equal(K + "LookAt", lookAt.Name);
        var first = (XElement)lookAt.FirstNode!;
        Assert.Equal(Gx + "TimeStamp", first.Name);
        Assert.Equal("2026-12-01T23:30:00Z", first.Element(K + "when")!.Value);
        Assert.Equal(
            ["longitude", "latitude", "altitude", "heading", "tilt", "range", "altitudeMode"],
            lookAt.Elements().Skip(1).Select(e => e.Name.LocalName).ToArray());
        Assert.Equal("-97.155000", lookAt.Element(K + "longitude")!.Value);
        Assert.Equal("25.997000", lookAt.Element(K + "latitude")!.Value);
        Assert.Equal("0", lookAt.Element(K + "altitude")!.Value);
        Assert.Equal("90", lookAt.Element(K + "heading")!.Value);
        Assert.Equal("60", lookAt.Element(K + "tilt")!.Value);
        Assert.Equal("60000", lookAt.Element(K + "range")!.Value);
        Assert.Equal("absolute", lookAt.Element(K + "altitudeMode")!.Value);
    }

    [Fact]
    public void WithoutClock_HasNoTimeElement()
    {
        var lookAt = MissionLookAt.Create(25.997, -97.155);

        Assert.Empty(lookAt.Descendants(Gx + "TimeStamp"));
        Assert.Equal(7, lookAt.Elements().Count());
    }

    [Fact]
    public void Offset_ShiftsTimeStamp()
    {
        var lookAt = MissionLookAt.Create(25.997, -97.155, Clock, offsetSeconds: 75);

        Assert.Equal("2026-12-01T23:31:15Z", lookAt.Descendants(K + "when").Single().Value);
    }

    [Fact]
    public void CustomView_IsUsed()
    {
        var lookAt = MissionLookAt.Create(1, 2, rangeMeters: 5000, tiltDegrees: 30, headingDegrees: 10);

        Assert.Equal("5000", lookAt.Element(K + "range")!.Value);
        Assert.Equal("30", lookAt.Element(K + "tilt")!.Value);
        Assert.Equal("10", lookAt.Element(K + "heading")!.Value);
    }
}
