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
        return EventPlacemarkBuilder.CreateFolder(events, clock, EventPlacemarkOptions.Kmz);
    }

    public static XElement CreatePlacemark(MissionEvent mission, LaunchClock clock)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(clock);
        return EventPlacemarkBuilder.CreatePlacemark(mission, clock, EventPlacemarkOptions.Kmz);
    }
}
