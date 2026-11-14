using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Interfaces;

public interface IMissionKmlBuilder
{
    /// <summary>Builds the doc.kml text for the mission (deterministic; times = Clock T0 + offset; lon,lat,alt order).</summary>
    string Build(Mission mission);
}
