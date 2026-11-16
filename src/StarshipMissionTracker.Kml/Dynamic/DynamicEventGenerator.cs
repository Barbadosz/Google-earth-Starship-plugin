using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;

namespace StarshipMissionTracker.Kml.Dynamic;

/// <summary>
/// Mission event Placemarks of the server mode. Unlike the KMZ events they carry no TimeStamp (the time slider
/// of a refreshing NetworkLink must not hide them); T0 + offset is shown in the description instead.
/// Built by the shared <see cref="EventPlacemarkBuilder"/>.
/// </summary>
internal static class DynamicEventGenerator
{
    public static XElement CreateFolder(IEnumerable<MissionEvent> events, LaunchClock clock) =>
        EventPlacemarkBuilder.CreateFolder(events, clock, EventPlacemarkOptions.Dynamic);

    public static XElement CreatePlacemark(MissionEvent mission, LaunchClock clock) =>
        EventPlacemarkBuilder.CreatePlacemark(mission, clock, EventPlacemarkOptions.Dynamic);
}
