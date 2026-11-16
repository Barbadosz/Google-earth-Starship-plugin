using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// Toggle folder "Event labels": one label-only Placemark ("T+hh:mm:ss Name") per event with coordinates.
/// Thin wrapper over <see cref="EventPlacemarkBuilder"/> for the KMZ (TimeStamp) and dynamic (no TimeStamp) builders.
/// </summary>
internal static class EventLabelGenerator
{
    /// <param name="timeStamp">true = KMZ variant (TimeStamp on every label); false = dynamic/server variant.</param>
    public static XElement CreateFolder(IEnumerable<MissionEvent> events, LaunchClock clock, bool timeStamp)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clock);
        return EventPlacemarkBuilder.CreateLabelFolder(
            events, clock, timeStamp ? EventPlacemarkOptions.Kmz : EventPlacemarkOptions.Dynamic);
    }
}
