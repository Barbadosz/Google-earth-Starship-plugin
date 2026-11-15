using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Kml.Generators;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Styles;

/// <summary>
/// The single place for KML colours and Style definitions. Colours are KML ARGB in <c>aabbggrr</c> order.
/// Style IDs come from <see cref="KmlLod.StyleId"/>; every ID used by the generators is defined here.
/// </summary>
public static class KmlStyles
{
    /// <summary>Planned path: blue.</summary>
    public const string PlannedColor = "ffff0000";

    /// <summary>Planned Super Heavy path: a lighter blue, so the two planned paths can be told apart.</summary>
    public const string PlannedSuperHeavyColor = "ffff8000";

    /// <summary>Reconstructed path: pale blue (still "planned/reconstructed", never the measured green).</summary>
    public const string ReconstructedColor = "ffffc080";

    /// <summary>Actual (measured/published) path: green.</summary>
    public const string ActualColor = "ff00ff00";

    /// <summary>Estimated/simulated parts of an actual path: orange.</summary>
    public const string EstimatedColor = "ff0080ff";

    /// <summary>Mission event marker: yellow.</summary>
    public const string EventColor = "ff00ffff";

    /// <summary>Alpha used for the extruded curtain below a path (PolyStyle).</summary>
    public const string CurtainAlpha = "40";

    public const double PathLineWidth = 3;
    public const double IconScale = 1.0;
    public const double EventIconScale = 0.9;

    /// <summary>Icon file of a vehicle inside the KMZ (relative to the KMZ root).</summary>
    public const string StarshipIconHref = "files/icons/starship.png";

    /// <summary>Icon file of the Super Heavy booster inside the KMZ (relative to the KMZ root).</summary>
    public const string SuperHeavyIconHref = "files/icons/booster.png";

    /// <summary>Event marker: Google Earth's built-in circle shape (referenced, not bundled; needs internet access to be drawn).</summary>
    public const string EventIconHref = "https://maps.google.com/mapfiles/kml/shapes/placemark_circle.png";

    /// <summary>KMZ entry paths of the icon files the styles reference (the packager must include them).</summary>
    public static IReadOnlyList<string> IconHrefs { get; } = [StarshipIconHref, SuperHeavyIconHref];

    /// <summary>Icon href (inside the KMZ) of a vehicle type.</summary>
    public static string IconHref(VehicleType vehicle) =>
        vehicle == VehicleType.Starship ? StarshipIconHref : SuperHeavyIconHref;

    /// <summary>All Style elements, in a fixed order (deterministic output).</summary>
    public static IReadOnlyList<XElement> CreateAll() =>
    [
        PathStyle(KmlLod.StyleId.PlannedStarship, PlannedColor),
        PathStyle(KmlLod.StyleId.PlannedSuperHeavy, PlannedSuperHeavyColor),
        PathStyle(KmlLod.StyleId.ReconstructedStarship, ReconstructedColor),
        PathStyle(KmlLod.StyleId.ReconstructedSuperHeavy, ReconstructedColor),
        PathStyle(KmlLod.StyleId.ActualStarship, ActualColor),
        PathStyle(KmlLod.StyleId.ActualSuperHeavy, ActualColor),
        PathStyle(KmlLod.StyleId.EstimatedStarship, EstimatedColor),
        PathStyle(KmlLod.StyleId.EstimatedSuperHeavy, EstimatedColor),
        IconStyle(KmlLod.StyleId.IconStarship, StarshipIconHref),
        IconStyle(KmlLod.StyleId.IconSuperHeavy, SuperHeavyIconHref),
        EventStyle(),
    ];

    /// <summary>
    /// Same styles as <see cref="CreateAll()"/>, but the vehicle icons point to
    /// <c>{iconBaseHref}/starship.png</c> and <c>{iconBaseHref}/booster.png</c> (server mode: icons served over HTTP).
    /// </summary>
    public static IReadOnlyList<XElement> CreateAll(string iconBaseHref)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconBaseHref);
        var baseHref = iconBaseHref.TrimEnd('/');
        var styles = CreateAll().ToList();
        for (var i = 0; i < styles.Count; i++)
        {
            var id = styles[i].Attribute("id")!.Value;
            if (id == KmlLod.StyleId.IconStarship)
            {
                styles[i] = IconStyle(id, $"{baseHref}/{Path.GetFileName(StarshipIconHref)}");
            }
            else if (id == KmlLod.StyleId.IconSuperHeavy)
            {
                styles[i] = IconStyle(id, $"{baseHref}/{Path.GetFileName(SuperHeavyIconHref)}");
            }
        }

        return styles;
    }

    /// <summary>The colour of the path style with the given ID (for tests and legends).</summary>
    public static string PathColor(string styleId) => styleId switch
    {
        KmlLod.StyleId.PlannedStarship => PlannedColor,
        KmlLod.StyleId.PlannedSuperHeavy => PlannedSuperHeavyColor,
        KmlLod.StyleId.ReconstructedStarship or KmlLod.StyleId.ReconstructedSuperHeavy => ReconstructedColor,
        KmlLod.StyleId.ActualStarship or KmlLod.StyleId.ActualSuperHeavy => ActualColor,
        KmlLod.StyleId.EstimatedStarship or KmlLod.StyleId.EstimatedSuperHeavy => EstimatedColor,
        _ => throw new ArgumentException($"'{styleId}' is not a path style ID.", nameof(styleId)),
    };

    // Used by the static LineString and by the gx:Track trail: IconStyle/LabelStyle scale 0 hide the
    // point marker Google Earth would otherwise draw at the current track position.
    private static XElement PathStyle(string id, string color) =>
        K("Style", new XAttribute("id", id),
            K("IconStyle", K("scale", "0")),
            K("LabelStyle", K("scale", "0")),
            K("LineStyle", K("color", color), K("width", Number(PathLineWidth))),
            K("PolyStyle", K("color", CurtainAlpha + color[2..])));

    private static XElement IconStyle(string id, string href) =>
        K("Style", new XAttribute("id", id),
            K("IconStyle",
                K("scale", Number(IconScale)),
                K("Icon", K("href", href)),
                K("hotSpot",
                    new XAttribute("x", "0.5"), new XAttribute("y", "0.5"),
                    new XAttribute("xunits", "fraction"), new XAttribute("yunits", "fraction"))),
            K("LabelStyle", K("scale", "0.8")));

    private static XElement EventStyle() =>
        K("Style", new XAttribute("id", KmlLod.StyleId.Event),
            K("IconStyle",
                K("color", EventColor),
                K("scale", Number(EventIconScale)),
                K("Icon", K("href", EventIconHref))),
            K("LabelStyle", K("scale", "0.8")));
}
