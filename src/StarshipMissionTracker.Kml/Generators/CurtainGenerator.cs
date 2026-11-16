using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// Toggleable "Flight path curtain" layer (N-39a): the vertical extrude walls under the flight path as their own
/// folder, so the path line itself can be emitted without extrude. The curtain duplicates the path coordinates.
/// </summary>
internal static class CurtainGenerator
{
    /// <summary>
    /// Folder <c>id=curtain</c>, name "Flight path curtain", style <c>#toggle-folder</c>, one curtain Placemark per
    /// (vehicle, path). Paths without points are skipped (no empty LineString).
    /// </summary>
    public static XElement CreateFolder(IEnumerable<(Vehicle Vehicle, FlightPath Path)> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return K("Folder",
            new XAttribute("id", KmlLod.FolderId.Curtain),
            K("name", KmlLod.FolderName.FlightCurtain),
            K("styleUrl", "#" + KmlLod.ToggleStyleId.ToggleFolder),
            paths.Where(p => p.Path.Points.Count > 0).Select(p => CreatePlacemark(p.Vehicle, p.Path)));
    }

    /// <summary>
    /// Placemark with an extruded LineString (absolute, extrude 1, tessellate 0) and the curtain style; the
    /// coordinates are identical to <see cref="PlannedPathGenerator"/>'s. Throws if the path has no points.
    /// </summary>
    public static XElement CreatePlacemark(Vehicle vehicle, FlightPath path)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Points.Count == 0)
        {
            throw new ArgumentException("The flight path has no points.", nameof(path));
        }

        var coordinates = string.Join(
            " ",
            path.Points.Select(p => Coordinates(p.Latitude, p.Longitude, p.AltitudeMeters)));

        return K("Placemark",
            K("name", $"{vehicle.Name} – {KmlPathInfo.Kind(path)} curtain"),
            K("styleUrl", "#" + KmlLod.CurtainStyleId(path.Type, vehicle.Type, path.Quality)),
            K("LineString",
                K("extrude", "1"),
                K("tessellate", "0"),
                K("altitudeMode", "absolute"),
                K("coordinates", coordinates)));
    }
}
