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
        var vehicles = mission.Vehicles.OrderBy(v => v.Type).ToList();
        foreach (var vehicle in vehicles)
        {
            AddPaths(planned, vehicle, vehicle.FlightPaths.Where(IsPlannedPath), densified, extrude: false);
        }

        // N-39a: the curtain of the planned path is its own toggle folder (same paths, no densification note).
        var curtain = CurtainGenerator.CreateFolder(
            vehicles.SelectMany(v => v.FlightPaths.Where(IsPlannedPath).Select(p => (v, p))));

        var document = K("Document",
            K("name", $"Planned trajectory & events – {info.Name}"),
            K("description", Cdata(SimulatedHtml(info, "Planned / Reconstructed – not live telemetry."))),
            KmlStyles.CreateAll(assets.IconBaseUrl),
            KmlStyles.CreateToggleStyles(),
            DynamicMissionInfoGenerator.CreateFolder(info, mission, controlUrl),
            planned,
            curtain,
            DynamicEventGenerator.CreateFolder(mission.Config.Events, mission.Clock),
            EventLabelGenerator.CreateFolder(mission.Config.Events, mission.Clock, timeStamp: false));
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
            AddPaths(actual, vehicle, vehicle.FlightPaths.Where(p => p.Type == TrajectoryType.Actual), empty, extrude: true);
        }

        var document = K("Document",
            K("name", $"Actual trajectory – {info.Name}"),
            K("description", Cdata(SimulatedHtml(info, "Actual path as recorded so far; estimated data is not measured telemetry."))),
            actual);
        ScopeStyles(document, HistoryStylePrefix, assets);
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
            LiveVehicleGenerator.CreateFolder(info, vehicles, assets));
        ScopeStyles(document, LiveStylePrefix, assets);
        return Serialize(Root(control, document));
    }

    /// <summary>Style id prefix of the history document (N-54: ids unique per document, so a reload never re-resolves the planned styles).</summary>
    public const string HistoryStylePrefix = "history-";

    /// <summary>Style id prefix of the live document (N-54).</summary>
    public const string LiveStylePrefix = "live-";

    /// <summary>
    /// N-54: adds to <paramref name="document"/> only the styles its features reference, with ids prefixed by
    /// <paramref name="prefix"/>, and rewrites the features' <c>styleUrl</c>s to match. Colours and icons are unchanged.
    /// </summary>
    private static void ScopeStyles(XElement document, string prefix, KmlAssetUrls assets)
    {
        var styleUrls = document.Descendants(KmlXml.Kml + "styleUrl")
            .Where(u => u.Value.StartsWith('#'))
            .ToList();
        var used = styleUrls.Select(u => u.Value[1..]).ToHashSet(StringComparer.Ordinal);
        var styles = KmlStyles.CreateAll(assets.IconBaseUrl).Where(s => used.Contains(s.Attribute("id")!.Value)).ToList();
        foreach (var style in styles)
        {
            var id = style.Attribute("id")!;
            id.Value = prefix + id.Value;
        }

        var known = styles.Select(s => s.Attribute("id")!.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var url in styleUrls)
        {
            if (known.Contains(prefix + url.Value[1..]))
            {
                url.Value = "#" + prefix + url.Value[1..];
            }
        }

        // Styles go right after the description (same position as before), before the feature folders.
        var anchor = document.Element(KmlXml.Kml + "description");
        if (anchor is not null)
        {
            anchor.AddAfterSelf(styles);
        }
        else
        {
            document.AddFirst(styles);
        }
    }

    /// <summary>
    /// Path Placemarks (one per path) in a vehicle sub-folder; paths without points are skipped, empty vehicles omitted.
    /// <paramref name="extrude"/> false for planned paths (their curtain is the toggle folder); actual paths stay extruded.
    /// </summary>
    private static void AddPaths(
        XElement parent, Vehicle vehicle, IEnumerable<FlightPath> paths, IReadOnlySet<FlightPath> densified, bool extrude)
    {
        var vehicleFolder = K("Folder", K("name", VehicleFolderName(vehicle.Type)));
        foreach (var path in paths.Where(p => p.Points.Count > 0))
        {
            var placemark = PlannedPathGenerator.CreatePlacemark(vehicle, path, extrude);
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

    /// <summary>
    /// Initial view over the launch site with <c>gx:TimeStamp</c> = <see cref="MissionInfo.LaunchTimeUtc"/> (simulation
    /// anchor or planned T0, N-37; the same T0 as every T+ label), so opening the master puts the time slider and the Sun
    /// at T0 (N-40a). Only the master carries it: the child NetworkLinks use <c>flyToView=0</c>, so a refresh never moves
    /// the camera or the slider. The master is built per request, so a T0 change shows on the next open.
    /// </summary>
    private static XElement? LookAt(MissionInfo info) =>
        info.LaunchSite is { } site
            ? MissionLookAt.Create(site.Latitude, site.Longitude, new LaunchClock(info.LaunchTimeUtc))
            : null;

    private static bool IsPlannedPath(FlightPath path) => path.Type != TrajectoryType.Actual;
}
