using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// Static, always visible path line (no time): the whole flight path is shown without the time slider.
/// Works for any <see cref="FlightPath"/>; the style follows the path type and quality.
/// The line is thinned for display (N-20, <see cref="TrackThinner"/>): only points are dropped, never added or moved.
/// </summary>
public static class PlannedPathGenerator
{
    /// <summary>Placemark with a LineString (absolute, extrude 1, tessellate 0).</summary>
    public static XElement CreatePlacemark(Vehicle vehicle, FlightPath path) => CreatePlacemark(vehicle, path, extrude: true);

    /// <summary>
    /// Same Placemark; <paramref name="extrude"/> false emits <c>extrude 0</c> (the curtain is then a separate layer,
    /// see <see cref="CurtainGenerator"/>). <c>true</c> is byte-identical to the two-argument overload.
    /// <paramref name="displayPoints"/>: the already thinned points of <paramref name="path"/> (the caller thinned once
    /// for several layers); null thins here with <see cref="KmlLod.TrailToleranceMeters"/>.
    /// </summary>
    public static XElement CreatePlacemark(
        Vehicle vehicle, FlightPath path, bool extrude, IReadOnlyList<TrajectoryPoint>? displayPoints = null)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Points.Count == 0)
        {
            throw new ArgumentException("The flight path has no points.", nameof(path));
        }

        var points = KmlPathInfo.ResolveDisplayPoints(path, displayPoints);
        var coordinates = string.Join(
            " ",
            points.Select(p => Coordinates(p.Latitude, p.Longitude, p.AltitudeMeters)));

        return K("Placemark",
            K("name", $"{vehicle.Name} – {KmlPathInfo.Kind(path)} path"),
            K("description", KmlPathInfo.Description(path, KmlPathInfo.IsSimplified(path, points))),
            K("styleUrl", "#" + KmlLod.PathStyleId(path.Type, vehicle.Type, path.Quality)),
            KmlPathInfo.Extended(vehicle, path),
            K("LineString",
                K("extrude", extrude ? "1" : "0"),
                K("tessellate", "0"),
                K("altitudeMode", "absolute"),
                K("coordinates", coordinates)));
    }
}
