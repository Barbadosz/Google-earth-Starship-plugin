using System.Text;
using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using static StarshipMissionTracker.Kml.Dynamic.DynamicKmlWriter;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Dynamic;

/// <summary>Folder "Mission Information" of the planned document: T0, simulation notice, worst data quality, optional control URL.</summary>
internal static class DynamicMissionInfoGenerator
{
    public const string SimulationNotice =
        "SIMULATED: positions come from an internal simulator replaying planned data, not from live telemetry.";

    public static XElement CreateFolder(MissionInfo info, Mission mission, string? controlUrl)
    {
        var placemark = K("Placemark",
            K("name", info.Name),
            K("description", Cdata(DescriptionHtml(info, mission, controlUrl))));
        if (LaunchSite(info, mission) is { } site)
        {
            placemark.Add(K("Point",
                K("altitudeMode", "clampToGround"),
                K("coordinates", Coordinates(site.Latitude, site.Longitude, null))));
        }

        return K("Folder", K("name", KmlLod.FolderName.MissionInformation), placemark);
    }

    private static string DescriptionHtml(MissionInfo info, Mission mission, string? controlUrl)
    {
        var quality = WorstQuality(mission);
        var sb = new StringBuilder();
        sb.Append("<p><b>").Append(KmlXml.Html(info.Name)).Append("</b> (").Append(KmlXml.Html(info.MissionCode)).Append(")</p>");
        sb.Append("<p>Launch time (T0): ").Append(KmlXml.Html(mission.Clock.Format(0)))
            .Append(" UTC – planned launch time, subject to change.</p>");
        sb.Append("<p>Status: ").Append(KmlXml.Html(info.Status.ToString())).Append("</p>");
        if (!string.IsNullOrWhiteSpace(info.Description))
        {
            sb.Append("<p>").Append(KmlXml.Html(info.Description)).Append("</p>");
        }

        if (info.IsSimulated)
        {
            sb.Append("<p><b>").Append(KmlXml.Html(SimulationNotice)).Append("</b></p>");
        }

        sb.Append("<p>Planned / Reconstructed – not live telemetry.<br/>")
            .Append(KmlXml.Html(QualityNote(quality))).Append(", lowest in this document.</p>");
        if (!string.IsNullOrWhiteSpace(controlUrl))
        {
            sb.Append("<p>Simulation control: ").Append(KmlXml.Html(controlUrl)).Append("</p>");
        }

        return sb.ToString();
    }

    private static DataQuality WorstQuality(Mission mission)
    {
        var qualities = mission.Vehicles.SelectMany(v => v.FlightPaths).Select(p => p.Quality)
            .Concat(mission.Config.Events.Select(e => e.Quality))
            .ToList();
        return qualities.Count == 0 ? DataQuality.Unknown : qualities.Aggregate(DataQualityExtensions.Worst);
    }

    /// <summary>Launch site: the mission header, else the Liftoff event, else the first path point.</summary>
    internal static (double Latitude, double Longitude)? LaunchSite(MissionInfo info, Mission mission)
    {
        if (info.LaunchSite is { } known)
        {
            return known;
        }

        var liftoff = mission.Config.Events.FirstOrDefault(e => e.Type == EventType.Liftoff && e.HasPosition);
        if (liftoff is { Latitude: { } lat, Longitude: { } lon })
        {
            return (lat, lon);
        }

        var first = mission.Vehicles.OrderBy(v => v.Type).SelectMany(v => v.FlightPaths).FirstOrDefault(p => p.Points.Count > 0);
        return first is null ? null : (first.Points[0].Latitude, first.Points[0].Longitude);
    }
}
