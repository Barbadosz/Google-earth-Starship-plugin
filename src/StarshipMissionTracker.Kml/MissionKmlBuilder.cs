using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using StarshipMissionTracker.Kml.Styles;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml;

/// <summary>
/// Assembles the whole KML document of a mission (spec 9.1): Mission Information, Planned Trajectory,
/// Actual Trajectory, Vehicles (3D model + icon per vehicle) and Mission Events, plus all Styles and an
/// initial view over the launch site at T0. The points must already be prepared (densified, orientation
/// filled) by the caller; this class only writes XML. Output is deterministic (fixed order, "\n" line ends).
/// </summary>
public sealed class MissionKmlBuilder : IMissionKmlBuilder
{
    public const string DocumentTitle = "Starship Mission Tracker";

    /// <summary>Distance of the initial view from the launch site, metres.</summary>
    public const double InitialViewRangeMeters = 60000;

    /// <summary>Initial view tilt (0 = straight down), degrees.</summary>
    public const double InitialViewTiltDegrees = 60;

    /// <summary>Initial view heading (east, the usual launch direction from the Gulf coast), degrees.</summary>
    public const double InitialViewHeadingDegrees = 90;

    private readonly TrajectoryDensificationOptions? _densification;
    private readonly IReadOnlySet<FlightPath>? _densifiedPaths;

    /// <param name="densification">The options the caller densified the paths with, or null if the paths were not densified.</param>
    /// <param name="densifiedPaths">
    /// The paths (compared by reference) that really got points inserted. Only those path/track descriptions say
    /// that inserted points are interpolated and the quality is at most Estimated; no note without both arguments.
    /// </param>
    public MissionKmlBuilder(TrajectoryDensificationOptions? densification = null, IReadOnlySet<FlightPath>? densifiedPaths = null)
    {
        _densification = densification;
        _densifiedPaths = densifiedPaths;
    }

    public string Build(Mission mission) => Serialize(BuildDocument(mission));

    /// <summary>The kml root element (with the gx namespace declared).</summary>
    public XElement BuildDocument(Mission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var clock = mission.Clock;

        var document = K("Document",
            K("name", $"{DocumentTitle} – {mission.Name}"),
            K("open", "1"),
            K("description", Cdata(MissionHtml(mission))),
            LookAt(mission),
            KmlStyles.CreateAll(),
            KmlStyles.CreateToggleStyles(),
            MissionInformationFolder(mission),
            TrajectoryFolder(mission, KmlLod.FolderName.PlannedTrajectory, IsPlannedFolderPath,
                "Planned / Reconstructed – not live telemetry.", extrude: false),
            CurtainGenerator.CreateFolder(OrderedVehicles(mission)
                .SelectMany(v => v.FlightPaths.Where(IsPlannedFolderPath).Select(p => (v, p)))),
            TrajectoryFolder(mission, KmlLod.FolderName.ActualTrajectory, p => p.Type == TrajectoryType.Actual,
                "Actual-type paths (see the quality note on each path). Empty when no actual trajectory is available.",
                extrude: true),
            VehiclesFolder(mission),
            EventGenerator.CreateFolder(mission.Config.Events, clock),
            EventLabelGenerator.CreateFolder(mission.Config.Events, clock, timeStamp: true));

        return K("kml",
            new XAttribute(XNamespace.Xmlns + "gx", Gx.NamespaceName),
            document);
    }

    private static bool IsPlannedFolderPath(FlightPath path) => path.Type != TrajectoryType.Actual;

    /// <param name="extrude">
    /// false for the planned folder: its curtain is the separate toggle folder "Flight path curtain" (N-39a).
    /// Actual paths keep the extruded LineString (the plan has a curtain layer only for the planned path).
    /// </param>
    private XElement TrajectoryFolder(Mission mission, string name, Func<FlightPath, bool> include, string note, bool extrude)
    {
        var folder = K("Folder", K("name", name), K("description", note));
        foreach (var vehicle in OrderedVehicles(mission))
        {
            var paths = vehicle.FlightPaths.Where(include).ToList();
            if (paths.Count == 0)
            {
                continue;
            }

            var vehicleFolder = K("Folder", K("name", VehicleFolderName(vehicle.Type)));
            foreach (var path in paths)
            {
                vehicleFolder.Add(
                    AppendDensificationNote(PlannedPathGenerator.CreatePlacemark(vehicle, path, extrude), path),
                    AppendDensificationNote(GxTrackGenerator.CreatePlacemark(vehicle, path, mission.Clock), path));
            }

            folder.Add(vehicleFolder);
        }

        return folder;
    }

    private XElement VehiclesFolder(Mission mission)
    {
        var folder = K("Folder", K("name", KmlLod.FolderName.Vehicles));
        foreach (var vehicle in OrderedVehicles(mission))
        {
            var path = FollowedPath(vehicle);
            folder.Add(K("Folder",
                K("name", VehicleFolderName(vehicle.Type)),
                VehicleGenerator.CreateModelFolder(vehicle, path, mission.Clock),
                VehicleGenerator.CreateIconFolder(vehicle, path, mission.Clock)));
        }

        return folder;
    }

    /// <summary>The vehicle model/icon follow the actual path when there is one, otherwise the first (planned) path.</summary>
    public static FlightPath FollowedPath(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        return vehicle.FlightPaths.FirstOrDefault(p => p.Type == TrajectoryType.Actual) ?? vehicle.FlightPaths[0];
    }

    private static IEnumerable<Vehicle> OrderedVehicles(Mission mission) => mission.Vehicles.OrderBy(v => v.Type);

    private static string VehicleFolderName(VehicleType type) =>
        type == VehicleType.Starship ? KmlLod.FolderName.Starship : KmlLod.FolderName.SuperHeavy;

    /// <summary>Appends the densification paragraph to the CDATA description of a path/track Placemark (only if points were inserted).</summary>
    private XElement AppendDensificationNote(XElement placemark, FlightPath path)
    {
        if (_densification is null || _densifiedPaths is null || !_densifiedPaths.Contains(path))
        {
            return placemark;
        }

        var description = placemark.Element(KmlXml.Kml + "description");
        var html = (description?.Value ?? string.Empty) + $"<p>{Html(DensificationNote(_densification))}</p>";
        if (description is null)
        {
            placemark.AddFirst(K("description", Cdata(html)));
        }
        else
        {
            description.ReplaceNodes(Cdata(html));
        }

        return placemark;
    }

    /// <summary>Text telling the viewer that the path was densified and what that means for the data quality.</summary>
    public static string DensificationNote(TrajectoryDensificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var km = (options.MaxSpacingMeters / 1000).ToString("0.###", CultureInfo.InvariantCulture);
        var seconds = options.MaxSpacingSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return $"Densified for display along the WGS84 geodesic (spacing at most {km} km or {seconds} s). "
               + "Inserted points are interpolated, not source data; their quality is Estimated or worse, "
               + "so the path quality is downgraded to at most Estimated.";
    }

    private static XElement MissionInformationFolder(Mission mission)
    {
        var placemark = K("Placemark",
            K("name", mission.Name),
            K("description", Cdata(MissionHtml(mission) + TimelineHtml.Create(mission.Config.Events))));
        if (LaunchSite(mission) is { } site)
        {
            // N-40a hardening: double-click on Mission Information flies to the launch site with the slider (Sun) at T0.
            // KML order: AbstractView (LookAt) comes before the Geometry.
            placemark.Add(
                MissionLookAt.Create(site.Latitude, site.Longitude, mission.Clock),
                K("Point",
                K("altitudeMode", "clampToGround"),
                K("coordinates", Coordinates(site.Latitude, site.Longitude, null))));
        }

        return K("Folder", K("name", KmlLod.FolderName.MissionInformation), placemark);
    }

    private static string MissionHtml(Mission mission)
    {
        var quality = WorstQuality(mission);
        var t0 = mission.Clock.Format(0);
        var sb = new StringBuilder();
        sb.Append("<p><b>").Append(Html(mission.Name)).Append("</b> (").Append(Html(mission.MissionCode)).Append(")</p>");
        sb.Append("<p>Launch time (T0): ").Append(Html(t0)).Append(" UTC – planned launch time, subject to change.</p>");
        if (!string.IsNullOrWhiteSpace(mission.Config.Description))
        {
            sb.Append("<p>").Append(Html(mission.Config.Description)).Append("</p>");
        }

        sb.Append("<p>Planned / Reconstructed – not live telemetry.<br/>")
            .Append(Html(QualityNote(quality)))
            .Append(quality == DataQuality.Simulated ? ", lowest in this file: simulated / illustrative values." : ", lowest in this file.")
            .Append("</p>");
        return sb.ToString();
    }

    /// <summary>Worst quality over all paths and events (Unknown if there is nothing).</summary>
    private static DataQuality WorstQuality(Mission mission)
    {
        var qualities = mission.Vehicles.SelectMany(v => v.FlightPaths).Select(p => p.Quality)
            .Concat(mission.Config.Events.Select(e => e.Quality))
            .ToList();
        return qualities.Count == 0 ? DataQuality.Unknown : qualities.Aggregate(DataQualityExtensions.Worst);
    }

    /// <summary>Launch site: the position of the Liftoff event if known, otherwise the first point of the first path.</summary>
    private static (double Latitude, double Longitude)? LaunchSite(Mission mission)
    {
        var liftoff = mission.Config.Events.FirstOrDefault(e => e.Type == EventType.Liftoff && e.HasPosition);
        if (liftoff is { Latitude: { } lat, Longitude: { } lon })
        {
            return (lat, lon);
        }

        var first = OrderedVehicles(mission).SelectMany(v => v.FlightPaths).FirstOrDefault(p => p.Points.Count > 0);
        return first is null ? null : (first.Points[0].Latitude, first.Points[0].Longitude);
    }

    /// <summary>Initial view over the launch site; gx:TimeStamp puts the time slider (and the Sun) at T0.</summary>
    private static XElement? LookAt(Mission mission) =>
        LaunchSite(mission) is { } site ? MissionLookAt.Create(site.Latitude, site.Longitude, mission.Clock) : null;

    private static string Serialize(XElement root)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };
        using var writer = new Utf8StringWriter();
        using (var xml = XmlWriter.Create(writer, settings))
        {
            new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(xml);
        }

        return writer.ToString();
    }

    private sealed class Utf8StringWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
}
