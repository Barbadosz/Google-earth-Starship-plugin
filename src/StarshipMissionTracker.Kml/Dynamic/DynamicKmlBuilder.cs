using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Kml.Styles;
using static StarshipMissionTracker.Kml.Dynamic.DynamicKmlWriter;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Dynamic;

/// <summary>
/// Builds the server-mode KML documents (master with three NetworkLinks; planned, history and live child documents).
/// Pure: no network, no database, no clock (nowUtc is a parameter), no logging (the Api logs around the calls).
/// Each path is written once per document (N-20); child documents carry a NetworkLinkControl cookie with the revision (N-15).
/// Output is deterministic for the same input ("\n" line ends, invariant culture).
/// </summary>
public sealed class DynamicKmlBuilder : IDynamicKmlBuilder
{
    public const string DocumentTitle = MissionKmlBuilder.DocumentTitle;

    public const string DensifiedNote =
        "Densified for display along the WGS84 geodesic. Inserted points are interpolated, not source data; "
        + "their quality is Estimated or worse, so the path quality is at most Estimated.";

    public string BuildMaster(MissionInfo info, KmlEndpointUrls urls, NetworkLinkSettings settings, KmlAssetUrls? assets = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentNullException.ThrowIfNull(settings);
        // `assets` is unused on purpose: the master only holds NetworkLinks (no icons, styles or models). The parameter
        // stays so the IDynamicKmlBuilder.BuildMaster signature is stable for callers that already hold the asset URLs.
        var sim = info.IsSimulated;
        var document = K("Document",
            K("name", $"{DocumentTitle} – {info.Name}"),
            K("open", "1"),
            K("description", Cdata($"<p>{Html(info.Name)} ({Html(info.MissionCode)})</p>"
                                   + (sim ? $"<p><b>{Html(DynamicMissionInfoGenerator.SimulationNotice)}</b></p>" : string.Empty))),
            LookAt(info),
            NetworkLinkGenerator.Create("planned-link", "Planned trajectory & events",
                "Mission information, planned trajectory and events.", urls.PlannedUrl, settings.PlannedRefresh),
            NetworkLinkGenerator.Create("history-link", sim ? "Actual trajectory (SIMULATED)" : "Actual trajectory",
                "Actual trajectory so far.", urls.HistoryUrl, settings.HistoryRefresh),
            NetworkLinkGenerator.Create("live-link", sim ? "Vehicles (SIMULATED, live)" : "Vehicles (live)",
                "Vehicles at their latest sample.", urls.LiveUrl, settings.LiveRefresh));
        return Serialize(Root(null, document));
    }

    public string BuildPlanned(
        MissionInfo info,
        Mission mission,
        IReadOnlySet<FlightPath> densified,
        KmlAssetUrls assets,
        long revision,
        System.TimeSpan minRefresh,
        string? controlUrl = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(densified);
        ArgumentNullException.ThrowIfNull(assets);
        var control = NetworkLinkControl(revision, minRefresh);

        var planned = K("Folder",
            K("name", KmlLod.FolderName.PlannedTrajectory),
            K("description", "Planned / Reconstructed – not live telemetry."));
        foreach (var vehicle in mission.Vehicles.OrderBy(v => v.Type))
        {
            AddPaths(planned, vehicle, vehicle.FlightPaths.Where(p => p.Type != TrajectoryType.Actual), densified);
        }

        var document = K("Document",
            K("name", $"Planned trajectory & events – {info.Name}"),
            K("description", Cdata(SimulatedHtml(info, "Planned / Reconstructed – not live telemetry."))),
            KmlStyles.CreateAll(assets.IconBaseUrl),
            DynamicMissionInfoGenerator.CreateFolder(info, mission, controlUrl),
            planned,
            DynamicEventGenerator.CreateFolder(mission.Config.Events, mission.Clock));
        return Serialize(Root(control, document));
    }

    public string BuildHistory(
        MissionInfo info,
        IReadOnlyList<Vehicle> vehiclesWithActualPath,
        KmlAssetUrls assets,
        long revision,
        System.TimeSpan minRefresh)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(vehiclesWithActualPath);
        ArgumentNullException.ThrowIfNull(assets);
        var control = NetworkLinkControl(revision, minRefresh);

        var actual = K("Folder",
            K("name", KmlLod.FolderName.ActualTrajectory),
            K("description", "Actual-type paths (see the quality note on each path). Empty when no actual trajectory is available."));
        var empty = new HashSet<FlightPath>();
        foreach (var vehicle in vehiclesWithActualPath.OrderBy(v => v.Type))
        {
            AddPaths(actual, vehicle, vehicle.FlightPaths.Where(p => p.Type == TrajectoryType.Actual), empty);
        }

        var document = K("Document",
            K("name", $"Actual trajectory – {info.Name}"),
            K("description", Cdata(SimulatedHtml(info, "Actual path as recorded so far; estimated data is not measured telemetry."))),
            KmlStyles.CreateAll(assets.IconBaseUrl),
            actual);
        return Serialize(Root(control, document));
    }

    public string BuildLive(
        MissionInfo info,
        IReadOnlyList<LiveVehicleState> vehicles,
        DateTimeOffset nowUtc,
        KmlAssetUrls assets,
        long revision,
        System.TimeSpan minRefresh)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(assets);
        RequireUtc(nowUtc, nameof(nowUtc));
        var control = NetworkLinkControl(revision, minRefresh);

        var document = K("Document",
            K("name", $"Vehicles – {info.Name}"),
            K("description", Cdata(SimulatedHtml(info, $"Generated at {Utc(nowUtc)} UTC."))),
            KmlStyles.CreateAll(assets.IconBaseUrl),
            LiveVehicleGenerator.CreateFolder(info, vehicles, assets));
        return Serialize(Root(control, document));
    }

    /// <summary>Path Placemarks (one per path) in a vehicle sub-folder; paths without points are skipped, empty vehicles omitted.</summary>
    private static void AddPaths(XElement parent, Vehicle vehicle, IEnumerable<FlightPath> paths, IReadOnlySet<FlightPath> densified)
    {
        var vehicleFolder = K("Folder", K("name", VehicleFolderName(vehicle.Type)));
        foreach (var path in paths.Where(p => p.Points.Count > 0))
        {
            var placemark = PlannedPathGenerator.CreatePlacemark(vehicle, path);
            if (densified.Contains(path))
            {
                var description = placemark.Element(KmlXml.Kml + "description")!;
                description.ReplaceNodes(Cdata(description.Value + $"<p>{Html(DensifiedNote)}</p>"));
            }

            vehicleFolder.Add(placemark);
        }

        if (vehicleFolder.Elements(KmlXml.Kml + "Placemark").Any())
        {
            parent.Add(vehicleFolder);
        }
    }

    private static string SimulatedHtml(MissionInfo info, string text) =>
        (info.IsSimulated ? $"<p><b>{Html(DynamicMissionInfoGenerator.SimulationNotice)}</b></p>" : string.Empty)
        + $"<p>{Html(text)}</p>";

    /// <summary>Initial view over the launch site; no time element, so a refresh never moves the time slider.</summary>
    private static XElement? LookAt(MissionInfo info)
    {
        if (info.LaunchSite is not { } site)
        {
            return null;
        }

        return K("LookAt",
            K("longitude", Degrees(site.Longitude)),
            K("latitude", Degrees(site.Latitude)),
            K("altitude", "0"),
            K("heading", Number(MissionKmlBuilder.InitialViewHeadingDegrees)),
            K("tilt", Number(MissionKmlBuilder.InitialViewTiltDegrees)),
            K("range", Number(MissionKmlBuilder.InitialViewRangeMeters)),
            K("altitudeMode", "absolute"));
    }
}
