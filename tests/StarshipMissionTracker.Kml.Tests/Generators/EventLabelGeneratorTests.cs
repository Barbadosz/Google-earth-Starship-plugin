using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Kml.Tests.Generators;

public class EventLabelGeneratorTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly LaunchClock Clock = new(new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.Zero));

    private static MissionEvent Ev(double offset, string name = "Event", double? lat = 25.99, double? lon = -97.1, double? alt = 1000) =>
        new(offset, EventType.MaxQ, name, "desc", lat, lon, alt, VehicleType.Starship, DataQuality.Simulated, "src");

    private static string Name(XElement p) => p.Element(K + "name")!.Value;

    [Fact]
    public void Folder_HasIdNameAndToggleStyle()
    {
        var folder = EventLabelGenerator.CreateFolder([Ev(0)], Clock, timeStamp: true);

        Assert.Equal("event-labels", (string?)folder.Attribute("id"));
        Assert.Equal("Event labels", Name(folder));
        Assert.Equal("#toggle-folder", folder.Element(K + "styleUrl")!.Value);
    }

    [Theory]
    [InlineData(0, "T+00:00:00 Event")]
    [InlineData(75, "T+00:01:15 Event")]
    [InlineData(-10, "T-00:00:10 Event")]
    [InlineData(-3725, "T-01:02:05 Event")]
    [InlineData(90000, "T+25:00:00 Event")]
    public void Label_NameIsTLabelAndEventName(double offset, string expected)
    {
        var folder = EventLabelGenerator.CreateFolder([Ev(offset)], Clock, timeStamp: false);

        Assert.Equal(expected, Name(folder.Element(K + "Placemark")!));
    }

    [Fact]
    public void Label_XmlSpecialCharsInName_AreEscapedAndRoundTrip()
    {
        var folder = EventLabelGenerator.CreateFolder([Ev(5, "Stage <1> & \"Booster\"")], Clock, timeStamp: true);

        var xml = folder.ToString();
        Assert.Contains("&lt;1&gt; &amp;", xml);
        Assert.Equal("T+00:00:05 Stage <1> & \"Booster\"", Name(XElement.Parse(xml).Element(K + "Placemark")!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Label_StyleIsEventLabel_NoBalloon_PointMatchesEvent(bool timeStamp)
    {
        var placemark = EventLabelGenerator.CreateFolder([Ev(75)], Clock, timeStamp).Element(K + "Placemark")!;

        Assert.Equal("#event-label", placemark.Element(K + "styleUrl")!.Value);
        Assert.Null(placemark.Element(K + "description"));
        Assert.Null(placemark.Element(K + "ExtendedData"));
        var point = placemark.Element(K + "Point")!;
        Assert.Equal("-97.100000,25.990000,1000.0", point.Element(K + "coordinates")!.Value);
        Assert.Equal("absolute", point.Element(K + "altitudeMode")!.Value);
    }

    [Fact]
    public void Label_WithoutAltitude_ClampsToGround()
    {
        var placemark = EventLabelGenerator.CreateFolder([Ev(0, alt: null)], Clock, true).Element(K + "Placemark")!;

        var point = placemark.Element(K + "Point")!;
        Assert.Equal("clampToGround", point.Element(K + "altitudeMode")!.Value);
        Assert.Equal("-97.100000,25.990000", point.Element(K + "coordinates")!.Value);
    }

    [Fact]
    public void TimeStamp_OnlyInKmzVariant()
    {
        var kmz = EventLabelGenerator.CreateFolder([Ev(75)], Clock, timeStamp: true).Element(K + "Placemark")!;
        var dynamic = EventLabelGenerator.CreateFolder([Ev(75)], Clock, timeStamp: false).Element(K + "Placemark")!;

        Assert.Equal("2026-12-01T23:31:15Z", kmz.Element(K + "TimeStamp")!.Element(K + "when")!.Value);
        Assert.Null(dynamic.Element(K + "TimeStamp"));
    }

    [Fact]
    public void Folder_OrdersByOffset_AndSkipsEventsWithoutCoordinates()
    {
        var folder = EventLabelGenerator.CreateFolder(
            [Ev(100, "C"), Ev(50, "NoPos", null, null, null), Ev(-5, "A"), Ev(10, "B")], Clock, false);

        Assert.Equal(["T-00:00:05 A", "T+00:00:10 B", "T+00:01:40 C"],
            folder.Elements(K + "Placemark").Select(Name));
    }

    [Fact]
    public void EmptyEvents_GiveFolderWithoutPlacemarks()
    {
        var folder = EventLabelGenerator.CreateFolder([], Clock, true);

        Assert.Equal("event-labels", (string?)folder.Attribute("id"));
        Assert.Empty(folder.Elements(K + "Placemark"));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => EventLabelGenerator.CreateFolder(null!, Clock, true));
        Assert.Throws<ArgumentNullException>(() => EventLabelGenerator.CreateFolder([], null!, true));
    }
}
