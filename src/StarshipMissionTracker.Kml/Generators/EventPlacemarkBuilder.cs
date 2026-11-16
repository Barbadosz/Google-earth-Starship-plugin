using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// Variant switches of the shared event Placemark. Later options (label visibility, extrude line, T+ balloon table)
/// are added here as further init properties with defaults that keep the current output unchanged.
/// </summary>
internal sealed record EventPlacemarkOptions
{
    /// <summary>KMZ mode: emit a TimeStamp so the time slider drives the event.</summary>
    public bool TimeStamp { get; init; }

    /// <summary>Server mode: show the planned UTC time (T0 + offset) in the balloon instead of a TimeStamp.</summary>
    public bool PlannedTimeInDescription { get; init; }

    public static EventPlacemarkOptions Kmz { get; } = new() { TimeStamp = true };

    public static EventPlacemarkOptions Dynamic { get; } = new() { PlannedTimeInDescription = true };
}

/// <summary>Single implementation of the mission event Placemark used by both the KMZ and the server (dynamic) mode.</summary>
internal static class EventPlacemarkBuilder
{
    public static XElement CreateFolder(IEnumerable<MissionEvent> events, LaunchClock clock, EventPlacemarkOptions options) =>
        K("Folder",
            K("name", KmlLod.FolderName.MissionEvents),
            events.OrderBy(e => e.OffsetSeconds).Select(e => CreatePlacemark(e, clock, options)));

    /// <summary>
    /// Toggle folder "Event labels" (id <see cref="KmlLod.FolderId.EventLabels"/>, style #toggle-folder) with one label-only
    /// Placemark per event that has coordinates. Uses the same TimeStamp rule as the event folder of the same mode.
    /// </summary>
    public static XElement CreateLabelFolder(IEnumerable<MissionEvent> events, LaunchClock clock, EventPlacemarkOptions options) =>
        K("Folder",
            new XAttribute("id", KmlLod.FolderId.EventLabels),
            K("name", KmlLod.FolderName.EventLabels),
            K("styleUrl", "#" + KmlLod.ToggleStyleId.ToggleFolder),
            events.OrderBy(e => e.OffsetSeconds)
                .Where(e => e is { Latitude: not null, Longitude: not null })
                .Select(e => CreateLabelPlacemark(e, clock, options)));

    /// <summary>Label-only Placemark "T+hh:mm:ss Name" (style #event-label, no balloon) at the event position. Requires Latitude/Longitude.</summary>
    public static XElement CreateLabelPlacemark(MissionEvent missionEvent, LaunchClock clock, EventPlacemarkOptions options)
    {
        if (missionEvent is not { Latitude: { } lat, Longitude: { } lon })
        {
            throw new ArgumentException("The event has no coordinates.", nameof(missionEvent));
        }

        var placemark = K("Placemark", K("name", $"{TLabel(missionEvent.OffsetSeconds)} {missionEvent.Name}"));
        if (options.TimeStamp)
        {
            placemark.Add(K("TimeStamp", K("when", clock.Format(missionEvent.OffsetSeconds, NeedsMilliseconds([missionEvent.OffsetSeconds])))));
        }

        placemark.Add(
            K("styleUrl", "#" + KmlLod.ToggleStyleId.EventLabel),
            K("Point",
                K("altitudeMode", missionEvent.AltitudeMeters.HasValue ? "absolute" : "clampToGround"),
                K("coordinates", Coordinates(lat, lon, missionEvent.AltitudeMeters))));
        return placemark;
    }

    public static XElement CreatePlacemark(MissionEvent mission, LaunchClock clock, EventPlacemarkOptions options)
    {
        var ms = NeedsMilliseconds([mission.OffsetSeconds]);

        var html = $"<p><b>{Html(TLabel(mission.OffsetSeconds))}</b> {Html(mission.Name)}</p>"
                   + (options.PlannedTimeInDescription
                       ? $"<p>Planned time: {Html(clock.Format(mission.OffsetSeconds, ms))} UTC (T0 + offset, subject to change)</p>"
                       : string.Empty)
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
            K("description", Cdata(html)));
        if (options.TimeStamp)
        {
            placemark.Add(K("TimeStamp", K("when", clock.Format(mission.OffsetSeconds, ms))));
        }

        placemark.Add(K("styleUrl", "#" + KmlLod.StyleId.Event), ExtendedData(data.ToArray()));

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
