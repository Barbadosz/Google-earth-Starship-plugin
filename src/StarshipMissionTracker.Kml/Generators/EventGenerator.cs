using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>Mission event Placemarks (TimeStamp, optional Point, quality and source visible).</summary>
public static class EventGenerator
{
    /// <summary>Folder "Mission Events" with the events ordered by offset (stable).</summary>
    public static XElement CreateFolder(IEnumerable<MissionEvent> events, LaunchClock clock)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clock);
        return K("Folder",
            K("name", KmlLod.FolderName.MissionEvents),
            events.OrderBy(e => e.OffsetSeconds).Select(e => CreatePlacemark(e, clock)));
    }

    public static XElement CreatePlacemark(MissionEvent mission, LaunchClock clock)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(clock);
        var ms = NeedsMilliseconds([mission.OffsetSeconds]);

        var html = $"<p><b>{Html(TLabel(mission.OffsetSeconds))}</b> {Html(mission.Name)}</p>"
                   + (string.IsNullOrWhiteSpace(mission.Description) ? string.Empty : $"<p>{Html(mission.Description)}</p>")
                   + $"<p>{Html(QualityNote(mission.Quality))}<br/>Source: {Html(mission.SourceName)}</p>";

        var data = new List<(string, string)>
        {
            ("EventType", mission.Type.ToString()),
            ("OffsetSeconds", Number(mission.OffsetSeconds)),
            ("DataQuality", mission.Quality.ToString()),
            ("Source", mission.SourceName),
        };
        if (mission.Vehicle is { } vehicle)
        {
            data.Add(("Vehicle", vehicle.ToString()));
        }

        var placemark = K("Placemark",
            K("name", mission.Name),
            K("description", Cdata(html)),
            K("TimeStamp", K("when", clock.Format(mission.OffsetSeconds, ms))),
            K("styleUrl", "#" + KmlLod.StyleId.Event),
            ExtendedData(data.ToArray()));

        if (mission is { Latitude: { } lat, Longitude: { } lon })
        {
            // No altitude known: clamp to ground instead of inventing 0 m above sea level.
            placemark.Add(K("Point",
                K("altitudeMode", mission.AltitudeMeters.HasValue ? "absolute" : "clampToGround"),
                K("coordinates", Coordinates(lat, lon, mission.AltitudeMeters))));
        }

        return placemark;
    }
}
