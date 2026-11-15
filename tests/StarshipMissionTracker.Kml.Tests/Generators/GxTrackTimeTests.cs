using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Kml.Tests.Schema;

namespace StarshipMissionTracker.Kml.Tests.Generators;

/// <summary>N-22: timestamp precision of gx:Track (seconds, milliseconds, exact ticks) and its TimeSpan.</summary>
public class GxTrackTimeTests
{
    private static readonly XNamespace K = KmlXml.Kml;
    private static readonly XNamespace Gx = KmlXml.Gx;
    private static readonly LaunchClock Clock = new(new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.Zero));

    private static TrajectoryPoint Pt(double t) =>
        new(t, 25.997 + t * 1e-6, -97.157, 10, null, DataQuality.Reconstructed, "test source");

    private static (Vehicle Vehicle, FlightPath Path) Make(params double[] offsets)
    {
        var path = new FlightPath(
            TrajectoryType.Planned, DataQuality.Reconstructed, "test source", "CC0", offsets.Select(Pt).ToArray());
        return (new Vehicle("Starship", VehicleType.Starship, null, [path]), path);
    }

    private static (List<string> Whens, XElement Placemark) Render(params double[] offsets)
    {
        var (v, p) = Make(offsets);
        var placemark = GxTrackGenerator.CreatePlacemark(v, p, Clock);
        var whens = placemark.Element(Gx + "Track")!.Elements(K + "when").Select(w => w.Value).ToList();
        return (whens, placemark);
    }

    [Fact]
    public void NormalTrack_UsesWholeSeconds_NoFallback()
    {
        var (whens, placemark) = Render(0, 60, 120);
        Assert.Equal(["2026-12-01T23:30:00Z", "2026-12-01T23:31:00Z", "2026-12-01T23:32:00Z"], whens);
        var span = placemark.Element(K + "TimeSpan")!;
        Assert.Equal("2026-12-01T23:30:00Z", span.Element(K + "begin")!.Value);
        Assert.Equal("2026-12-01T23:32:00Z", span.Element(K + "end")!.Value);
    }

    [Fact]
    public void FractionalTrack_WithoutCollision_UsesMilliseconds()
    {
        var (whens, placemark) = Render(0, 0.5, 1.25);
        Assert.Equal(["2026-12-01T23:30:00.000Z", "2026-12-01T23:30:00.500Z", "2026-12-01T23:30:01.250Z"], whens);
        Assert.Equal("2026-12-01T23:30:01.250Z", placemark.Element(K + "TimeSpan")!.Element(K + "end")!.Value);
    }

    [Fact]
    public void CollidingTrack_SwitchesEntirelyToSevenDigits_AndSpanMatches()
    {
        var (whens, placemark) = Render(0, 0.0004, 5);
        Assert.Equal(whens.Count, whens.Distinct().Count());
        Assert.All(whens, w => Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z$", w));
        var span = placemark.Element(K + "TimeSpan")!;
        Assert.Equal(whens[0], span.Element(K + "begin")!.Value);
        Assert.Equal(whens[^1], span.Element(K + "end")!.Value);
        Assert.Equal("2026-12-01T23:30:05.0000000Z", span.Element(K + "end")!.Value);
    }

    [Fact]
    public void SubTickCollision_ThrowsMissionDataException_NamingVehicleAndOffsets()
    {
        var (v, p) = Make(0, 1, 1.00000001);
        var ex = Assert.Throws<MissionDataException>(() => GxTrackGenerator.CreatePlacemark(v, p, Clock));
        Assert.Contains("Starship", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1 s", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1.00000001 s", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SevenDigitWhenValues_ValidateAgainstKmlSchema()
    {
        var (_, placemark) = Render(0, 0.0004, 5);
        var kml = new XDocument(
            new XElement(K + "kml", new XAttribute(XNamespace.Xmlns + "gx", Gx.NamespaceName),
                new XElement(K + "Document", placemark))).ToString();
        Assert.Empty(KmlSchemaValidator.Validate(kml));
    }
}
