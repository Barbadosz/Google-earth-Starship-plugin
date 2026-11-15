using System.Xml.Linq;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Dynamic;

/// <summary>One NetworkLink of the master document: time based refresh, no camera jump, the user's checkbox state survives refreshes.</summary>
internal static class NetworkLinkGenerator
{
    public static XElement Create(string id, string name, string description, string href, System.TimeSpan refreshInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(href);
        if (refreshInterval <= System.TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshInterval), refreshInterval, "Refresh interval must be positive.");
        }

        return K("NetworkLink",
            new System.Xml.Linq.XAttribute("id", id),
            K("name", name),
            K("visibility", "1"),
            K("open", "0"),
            K("description", description),
            K("refreshVisibility", "0"),
            K("flyToView", "0"),
            K("Link",
                K("href", href),
                K("refreshMode", "onInterval"),
                K("refreshInterval", Number(refreshInterval.TotalSeconds))));
    }
}
