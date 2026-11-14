using System.Globalization;
using System.Net;
using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// Namespaces and shared XML building blocks of the KML generators. This is the only place
/// where latitude/longitude are swapped into KML order (lon first) and numbers are formatted.
/// </summary>
public static class KmlXml
{
    public static readonly XNamespace Kml = "http://www.opengis.net/kml/2.2";
    public static readonly XNamespace Gx = "http://www.google.com/kml/ext/2.2";

    internal static XElement K(string name, params object?[] content) => new(Kml + name, Sanitize(content));

    internal static XElement G(string name, params object?[] content) => new(Gx + name, Sanitize(content));

    private static object?[] Sanitize(object?[] content) =>
        content.Select(c => c is string s ? Clean(s) : c).ToArray();

    /// <summary>
    /// Removes characters that are illegal in XML 1.0 (control characters, lone surrogates, U+FFFE/FFFF),
    /// which would make the KML unreadable or throw on save. Valid surrogate pairs are kept.
    /// </summary>
    internal static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[++i]);
            }
            else if (System.Xml.XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    internal static string Degrees(double value) => Fixed(value, 6);

    internal static string Meters(double value) => Fixed(value, 1);

    internal static string Angle(double value) => Fixed(value, 2);

    internal static string Number(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Fixed(double value, int decimals)
    {
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            rounded = 0; // no "-0.0"
        }

        return rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    /// <summary>The single lat/lon to lon,lat swap for the comma form of a coordinate tuple.</summary>
    internal static string Coordinates(double latitude, double longitude, double? altitudeMeters) =>
        altitudeMeters is { } alt
            ? $"{Degrees(longitude)},{Degrees(latitude)},{Meters(alt)}"
            : $"{Degrees(longitude)},{Degrees(latitude)}";

    /// <summary>gx:coord form: space separated lon lat alt.</summary>
    internal static string GxCoordinates(TrajectoryPoint p) =>
        $"{Degrees(p.Longitude)} {Degrees(p.Latitude)} {Meters(p.AltitudeMeters)}";

    /// <summary>Whole-second timestamps unless some offset is fractional (then milliseconds for all).</summary>
    internal static bool NeedsMilliseconds(IEnumerable<double> offsets) =>
        offsets.Any(o => o != Math.Floor(o));

    internal static XElement ExtendedData(params (string Name, string Value)[] items) =>
        K("ExtendedData", items.Select(i => K("Data", new XAttribute("name", Clean(i.Name)), K("value", i.Value))));

    internal static string QualityNote(DataQuality quality) =>
        KmlLod.IsEstimated(quality)
            ? $"Data quality: {quality} (not measured telemetry)"
            : $"Data quality: {quality}";

    internal static XElement TimeSpan(LaunchClock clock, double beginOffset, double endOffset, bool milliseconds) =>
        K("TimeSpan",
            K("begin", clock.Format(beginOffset, milliseconds)),
            K("end", clock.Format(endOffset, milliseconds)));

    /// <summary>Smallest Region box side in degrees (about 110 m), so a one-point path is not zero-size.</summary>
    internal const double MinRegionSpanDegrees = 0.001;

    /// <summary>Smallest Region altitude range in metres.</summary>
    internal const double MinRegionAltitudeSpanMeters = 10;

    /// <summary>Region with the bounding box of the points (at least a minimum size) and the given Lod range.</summary>
    internal static XElement Region(IReadOnlyList<TrajectoryPoint> points, int minLodPixels, int maxLodPixels)
    {
        var (south, north) = Widen(points.Min(p => p.Latitude), points.Max(p => p.Latitude), MinRegionSpanDegrees);
        var (west, east) = Widen(points.Min(p => p.Longitude), points.Max(p => p.Longitude), MinRegionSpanDegrees);
        var (minAlt, maxAlt) = Widen(
            points.Min(p => p.AltitudeMeters), points.Max(p => p.AltitudeMeters), MinRegionAltitudeSpanMeters);
        return K("Region",
            K("LatLonAltBox",
                K("north", Degrees(Math.Min(north, 90))),
                K("south", Degrees(Math.Max(south, -90))),
                K("east", Degrees(Math.Min(east, 180))),
                K("west", Degrees(Math.Max(west, -180))),
                K("minAltitude", Meters(minAlt)),
                K("maxAltitude", Meters(maxAlt)),
                K("altitudeMode", "absolute")),
            K("Lod",
                K("minLodPixels", minLodPixels.ToString(CultureInfo.InvariantCulture)),
                K("maxLodPixels", maxLodPixels.ToString(CultureInfo.InvariantCulture))));
    }

    private static (double Min, double Max) Widen(double min, double max, double minimumSpan)
    {
        if (max - min >= minimumSpan)
        {
            return (min, max);
        }

        var centre = (min + max) / 2;
        return (centre - minimumSpan / 2, centre + minimumSpan / 2);
    }

    /// <summary>"T+00:02:12" / "T-00:00:30", whole seconds (truncated toward zero).</summary>
    internal static string TLabel(double offsetSeconds)
    {
        var total = (long)Math.Abs(Math.Truncate(offsetSeconds));
        var sign = offsetSeconds < 0 ? "-" : "+";
        return string.Create(CultureInfo.InvariantCulture,
            $"T{sign}{total / 3600:00}:{total % 3600 / 60:00}:{total % 60:00}");
    }

    internal static string Html(string? text) => WebUtility.HtmlEncode(Clean(text));

    internal static XCData Cdata(string html) => new(Clean(html).Replace("]]>", "]]&gt;", StringComparison.Ordinal));

    internal static string VehicleStem(VehicleType type) =>
        type == VehicleType.Starship ? "Starship" : "Super Heavy";
}
