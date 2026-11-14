using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Interfaces;

public interface IKmzPackager
{
    /// <summary>Writes a KMZ zip: doc.kml in the root, the files under their relative paths. Deterministic bytes.</summary>
    void Write(Stream output, string docKml, IReadOnlyList<KmzEntry> files);
}
