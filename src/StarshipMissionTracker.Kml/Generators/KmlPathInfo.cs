using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>Name, description and ExtendedData shared by the path and track Placemarks.</summary>
internal static class KmlPathInfo
{
    public static string Label(FlightPath path) => path.Type switch
    {
        TrajectoryType.Planned => "Planned / Reconstructed – not live telemetry",
        TrajectoryType.Reconstructed => "Reconstructed – not live telemetry",
        _ => KmlLod.IsEstimated(path.Quality) ? "Actual path, estimated – not measured telemetry" : "Actual path",
    };

    public static string Kind(FlightPath path) => path.Type.ToString().ToLowerInvariant();

    /// <summary>The track gets gx:angles only when every point has a full heading/pitch/roll.</summary>
    public static bool HasAngles(FlightPath path) => HasAngles(path.Points);

    /// <summary>True when the list is not empty and every point has a full heading/pitch/roll.</summary>
    public static bool HasAngles(IReadOnlyList<TrajectoryPoint> points) =>
        points.Count > 0
        && points.All(p => p.HeadingDegrees.HasValue && p.PitchDegrees.HasValue && p.RollDegrees.HasValue);

    /// <summary>
    /// Description of a path/trail Placemark. No orientation note: lines and the trail carry no gx:angles (only the
    /// model track does, N-20). <paramref name="simplified"/> adds <see cref="SimplifiedNote"/> (points were dropped).
    /// </summary>
    public static XCData Description(FlightPath path, bool simplified = false) =>
        Cdata($"<p>{Html(Label(path))}</p><p>{Html(QualityNote(path.Quality))}<br/>Source: {Html(path.SourceName)}"
              + (string.IsNullOrWhiteSpace(path.License) ? string.Empty : $"<br/>License: {Html(path.License)}")
              + (simplified ? $"<br/>{Html(SimplifiedNote)}" : string.Empty)
              + "</p>");

    public const string OrientationNote = "Orientation derived from the direction of travel (not measured attitude).";

    /// <summary>N-20: sentence on thinned display lines (trail, path), with <see cref="KmlLod.TrailToleranceMeters"/>.</summary>
    public static readonly string SimplifiedNote =
        $"Display line simplified (max {KmlLod.TrailToleranceMeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} m); "
        + "the vehicle follows the full-resolution data.";

    /// <summary>
    /// N-20: the points of a display line (trail, planned/actual path, curtain): <see cref="TrackThinner.Thin"/> with
    /// <see cref="KmlLod.TrailToleranceMeters"/>. A subset of the path's points (same instances), never new data.
    /// </summary>
    public static IReadOnlyList<TrajectoryPoint> DisplayPoints(FlightPath path) =>
        TrackThinner.Thin(path.Points, KmlLod.TrailToleranceMeters);

    /// <summary>The given display points, or the thinned path points when null. Throws if the list is empty.</summary>
    public static IReadOnlyList<TrajectoryPoint> ResolveDisplayPoints(FlightPath path, IReadOnlyList<TrajectoryPoint>? displayPoints)
    {
        var points = displayPoints ?? DisplayPoints(path);
        if (points.Count == 0)
        {
            throw new ArgumentException("The display point list is empty.", nameof(displayPoints));
        }

        return points;
    }

    /// <summary>True when the display line dropped points of the path (its description then gets <see cref="SimplifiedNote"/>).</summary>
    public static bool IsSimplified(FlightPath path, IReadOnlyList<TrajectoryPoint> displayPoints) =>
        displayPoints.Count < path.Points.Count;

    public static XElement Extended(Vehicle vehicle, FlightPath path)
    {
        var items = new List<(string, string)>
        {
            ("Vehicle", vehicle.Name),
            ("TrajectoryType", path.Type.ToString()),
            ("DataQuality", path.Quality.ToString()),
            ("Source", path.SourceName),
        };
        if (!string.IsNullOrWhiteSpace(path.License))
        {
            items.Add(("License", path.License));
        }

        items.Add(("PointCount", path.Points.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        items.Add(("OffsetSecondsStart", Number(path.Points[0].OffsetSeconds)));
        items.Add(("OffsetSecondsEnd", Number(path.Points[^1].OffsetSeconds)));
        return ExtendedData(items.ToArray());
    }
}
