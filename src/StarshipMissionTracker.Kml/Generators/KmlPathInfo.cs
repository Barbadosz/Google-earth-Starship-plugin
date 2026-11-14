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
    public static bool HasAngles(FlightPath path) =>
        path.Points.Count > 0
        && path.Points.All(p => p.HeadingDegrees.HasValue && p.PitchDegrees.HasValue && p.RollDegrees.HasValue);

    public static XCData Description(FlightPath path) =>
        Cdata($"<p>{Html(Label(path))}</p><p>{Html(QualityNote(path.Quality))}<br/>Source: {Html(path.SourceName)}"
              + (string.IsNullOrWhiteSpace(path.License) ? string.Empty : $"<br/>License: {Html(path.License)}")
              + (HasAngles(path) ? $"<br/>{Html(OrientationNote)}" : string.Empty)
              + "</p>");

    public const string OrientationNote = "Orientation derived from the direction of travel (not measured attitude).";

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
