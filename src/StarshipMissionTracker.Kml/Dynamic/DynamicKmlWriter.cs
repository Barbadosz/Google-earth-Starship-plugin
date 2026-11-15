using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Dynamic;

/// <summary>Shared helpers of the server-mode documents: root element, NetworkLinkControl, serialisation, text formats.</summary>
internal static class DynamicKmlWriter
{
    /// <summary>
    /// Root with the gx namespace declared. The optional NetworkLinkControl must come first (KML 2.2 schema order).
    /// </summary>
    public static XElement Root(XElement? networkLinkControl, XElement document) =>
        K("kml", new XAttribute(XNamespace.Xmlns + "gx", Gx.NamespaceName), networkLinkControl, document);

    /// <summary>
    /// <c>minRefreshPeriod</c> protects the server; <c>cookie</c> (<c>r=&lt;revision&gt;</c>) is appended by Google Earth to the next
    /// request's query, so the URL changes on every refresh (cache busting, N-15). Order as in the schema.
    /// </summary>
    public static XElement NetworkLinkControl(long revision, System.TimeSpan minRefresh)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "Revision must not be negative.");
        }

        if (minRefresh <= System.TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minRefresh), minRefresh, "Minimum refresh period must be positive.");
        }

        return K("NetworkLinkControl",
            K("minRefreshPeriod", Number(minRefresh.TotalSeconds)),
            K("cookie", "r=" + revision.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Vehicles in a fixed order (Starship first), so the output is deterministic.</summary>
    public static string VehicleFolderName(VehicleType type) =>
        type == VehicleType.Starship ? KmlLod.FolderName.Starship : KmlLod.FolderName.SuperHeavy;

    public static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(LaunchClock.MillisecondsFormat, CultureInfo.InvariantCulture);

    public static string Fixed(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    public static void RequireUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != System.TimeSpan.Zero)
        {
            throw new ArgumentException($"'{paramName}' must be a UTC instant (offset zero), got offset {value.Offset}.", paramName);
        }
    }

    public static string Serialize(XElement root)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };
        using var writer = new Utf8StringWriter();
        using (var xml = XmlWriter.Create(writer, settings))
        {
            new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(xml);
        }

        return writer.ToString();
    }

    private sealed class Utf8StringWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
}
