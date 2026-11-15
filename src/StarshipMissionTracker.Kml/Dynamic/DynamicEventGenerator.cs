using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using static StarshipMissionTracker.Kml.Dynamic.DynamicKmlWriter;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Dynamic;

/// <summary>
/// Mission event Placemarks of the server mode. Unlike the KMZ events they carry no TimeStamp (the time slider
/// of a refreshing NetworkLink must not hide them); T0 + offset is shown in the description instead.
/// </summary>
internal static class DynamicEventGenerator
{
    public static XElement CreateFolder(IEnumerable<MissionEvent> events, LaunchClock clock) =>
        K("Folder",
            K("name", KmlLod.FolderName.MissionEvents),
            events.OrderBy(e => e.OffsetSeconds).Select(e => CreatePlacemark(e, clock)));

    public static XElement CreatePlacemark(MissionEvent mission, LaunchClock clock)
    {
        var ms = NeedsMilliseconds([mission.OffsetSeconds]);
        var html = $"<p><b>{Html(TLabel(mission.OffsetSeconds))}</b> {Html(mission.Name)}</p>"
                   + $"<p>Planned time: {Html(clock.Format(mission.OffsetSeconds, ms))} UTC (T0 + offset, subject to change)</p>"
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
            K("styleUrl", "#" + KmlLod.StyleId.Event),
            ExtendedData(data.ToArray()));

        if (mission is { Latitude: { } lat, Longitude: { } lon })
        {
            placemark.Add(K("Point",
                K("altitudeMode", mission.AltitudeMeters.HasValue ? "absolute" : "clampToGround"),
                K("coordinates", Coordinates(lat, lon, mission.AltitudeMeters))));
        }

        return placemark;
    }
}
