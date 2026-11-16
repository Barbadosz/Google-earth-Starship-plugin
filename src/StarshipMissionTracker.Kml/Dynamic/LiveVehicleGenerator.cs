using System.Text;
using System.Xml.Linq;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Generators;
using static StarshipMissionTracker.Kml.Dynamic.DynamicKmlWriter;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Dynamic;

/// <summary>
/// Folder "Vehicles" of the live document: per vehicle a 3D model Placemark and an icon Placemark at the latest sample.
/// Deliberately untimed (no TimeStamp/TimeSpan), so the time slider never hides them.
/// </summary>
internal static class LiveVehicleGenerator
{
    /// <summary>Half side of the model's Region box around the sample, degrees (about 5 km). Tune in Google Earth (N-9).</summary>
    public const double ModelBoxHalfSpanDegrees = 0.05;

    /// <summary>Half height of the model's Region box, metres.</summary>
    public const double ModelBoxHalfHeightMeters = 1000;

    public const string WaitingText = "Waiting for liftoff";

    /// <summary>Name suffix of a vehicle shown at its planned pad position before the first telemetry sample.</summary>
    public const string PadSuffix = " (countdown, planned pad position)";

    public const string PadQuality = "Planned (countdown pad position)";

    public static XElement CreateFolder(MissionInfo info, IReadOnlyList<LiveVehicleState> vehicles, KmlAssetUrls assets)
    {
        // T+ source: the effective T0 (simulation anchor, else planned), the same T0 as the planned document, its events and
        // the history (Mission.Clock is built from it). BuildLive gets no Mission, so this MissionInfo value is the one
        // shared source; a moving actual T0 would make the same T+ label differ between the documents.
        var t0 = info.LaunchTimeUtc;
        var folder = K("Folder", K("name", KmlLod.FolderName.Vehicles));
        foreach (var state in vehicles.OrderBy(v => v.Vehicle.Type))
        {
            var vehicleFolder = K("Folder", K("name", VehicleFolderName(state.Vehicle.Type)));
            if (state.Latest is { HasPosition: true } sample)
            {
                var name = state.Vehicle.Name + (info.IsSimulated || sample.Quality == DataQuality.Simulated ? " (SIMULATED)" : string.Empty)
                           + (state.IsStale ? " – STALE" : string.Empty);
                var description = Description(info, name, state, sample, t0);
                vehicleFolder.Add(
                    ModelPlacemark(state.Vehicle, sample, name, description, assets),
                    IconPlacemark(state.Vehicle, sample, name + " icon", description));
            }
            else if (state.Latest is null && PadSample(state.Vehicle, t0) is { } pad)
            {
                // N-49: no telemetry yet (countdown) -> the model stands on the pad at the first planned point. It is the
                // planned position, labelled as such; no sample, velocity or "latest" is invented.
                var name = state.Vehicle.Name + (info.IsSimulated ? " (SIMULATED)" : string.Empty) + PadSuffix;
                var description = PadDescription(info, name);
                vehicleFolder.Add(
                    ModelPlacemark(state.Vehicle, pad, name, description, assets, PadExtended(state.Vehicle)),
                    IconPlacemark(state.Vehicle, pad, name + " icon", description, PadExtended(state.Vehicle)));
            }
            else
            {
                vehicleFolder.Add(K("description", WaitingText));
            }

            folder.Add(vehicleFolder);
        }

        return folder;
    }

    private static XElement ModelPlacemark(Vehicle vehicle, TelemetrySample s, string name, string description, KmlAssetUrls assets, XElement? extended = null)
    {
        var lat = s.Latitude!.Value;
        var lon = s.Longitude!.Value;
        var fileName = ModelFileName(vehicle);
        var model = K("Model", K("altitudeMode", s.AltitudeMeters.HasValue ? "absolute" : "clampToGround"));
        model.Add(K("Location",
            K("longitude", Degrees(lon)),
            K("latitude", Degrees(lat)),
            s.AltitudeMeters.HasValue ? K("altitude", Meters(s.AltitudeMeters.Value)) : null));
        if (s.HeadingDegrees is { } h && s.PitchDegrees is { } p && s.RollDegrees is { } r)
        {
            // Only a complete attitude: Google Earth reads a missing heading/tilt/roll as 0, which would fabricate data
            // (as KmlPathInfo.HasAngles). tilt = 90 - pitch, as in GxTrackGenerator.Angles.
            model.Add(K("Orientation", K("heading", Angle(h)), K("tilt", Angle(90 - p)), K("roll", Angle(r))));
        }

        model.Add(
            K("Scale", K("x", "1"), K("y", "1"), K("z", "1")),
            K("Link", K("href", assets.ModelBaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(fileName))));

        return K("Placemark",
            K("name", name),
            K("description", Cdata(description)),
            Region(BoxCorners(s), KmlLod.ModelMinLodPixels, KmlLod.ModelMaxLodPixels),
            extended ?? Extended(vehicle, s),
            model);
    }

    private static XElement IconPlacemark(Vehicle vehicle, TelemetrySample s, string name, string description, XElement? extended = null) =>
        K("Placemark",
            K("name", name),
            K("description", Cdata(description)),
            K("styleUrl", "#" + KmlLod.IconStyleId(vehicle.Type)),
            extended ?? Extended(vehicle, s),
            K("Point",
                K("altitudeMode", s.AltitudeMeters.HasValue ? "absolute" : "clampToGround"),
                K("coordinates", Coordinates(s.Latitude!.Value, s.Longitude!.Value, s.AltitudeMeters))));

    private static XElement Extended(Vehicle vehicle, TelemetrySample s) =>
        ExtendedData(
            ("Vehicle", vehicle.Name),
            ("DataQuality", s.Quality.ToString()),
            ("Source", s.SourceName),
            ("SampleTimeUtc", Utc(s.TimestampUtc)));

    /// <summary>Pad placemark data: explicitly planned, never the planned point's quality/source (could read as measured).</summary>
    private static XElement PadExtended(Vehicle vehicle) =>
        ExtendedData(
            ("Vehicle", vehicle.Name),
            ("DataQuality", PadQuality),
            ("Position", "Planned launch pad position (countdown), not telemetry"));

    /// <summary>The vehicle's first planned point as a position-only pseudo sample (null when it has no planned point).</summary>
    private static TelemetrySample? PadSample(Vehicle vehicle, DateTimeOffset t0)
    {
        var first = vehicle.FlightPaths
            .Where(p => p.Type != TrajectoryType.Actual && p.Points.Count > 0)
            .Select(p => p.Points.MinBy(pt => pt.OffsetSeconds)!)
            .MinBy(pt => pt.OffsetSeconds);
        return first is null
            ? null
            : new TelemetrySample(
                vehicle.Type, t0, t0, first.Latitude, first.Longitude, first.AltitudeMeters, null,
                first.HeadingDegrees, first.PitchDegrees, first.RollDegrees, first.Quality, first.SourceName);
    }

    private static string PadDescription(MissionInfo info, string name) =>
        "<p><b>" + Html(name) + "</b></p>"
        + "<p>Countdown: no telemetry yet. The vehicle is drawn at its planned launch pad position (planned data, not a measurement).</p>"
        + (info.IsSimulated ? "<p>Simulated mission – not live telemetry.</p>" : string.Empty);

    private static string Description(MissionInfo info, string name, LiveVehicleState state, TelemetrySample s, DateTimeOffset t0)
    {
        var sb = new StringBuilder();
        sb.Append("<p><b>").Append(Html(name)).Append("</b></p>");
        sb.Append("<p>Sample time: ").Append(Html(Utc(s.TimestampUtc))).Append(" UTC (")
            .Append(Html(TLabel((s.TimestampUtc - t0).TotalSeconds))).Append(")</p>");
        if (s.AltitudeMeters is { } alt)
        {
            sb.Append("<p>Altitude: ").Append(Html(Meters(alt))).Append(" m MSL");
            if (s.VelocityMetersPerSecond is { } v)
            {
                sb.Append("<br/>Velocity: ").Append(Html(Fixed(v, "0.#"))).Append(" m/s");
            }

            sb.Append("</p>");
        }
        else if (s.VelocityMetersPerSecond is { } speed)
        {
            sb.Append("<p>Velocity: ").Append(Html(Fixed(speed, "0.#"))).Append(" m/s</p>");
        }

        sb.Append("<p>")
            .Append(info.IsSimulated || s.Quality == DataQuality.Simulated
                ? "Simulated position – not live telemetry."
                : Html(QualityNote(s.Quality)))
            .Append("<br/>Source: ").Append(Html(s.SourceName)).Append("</p>");
        if (state.Age is { } age)
        {
            sb.Append("<p>Telemetry age: ").Append(Html(Fixed(age.TotalSeconds, "0.#"))).Append(" s");
            if (state.IsStale)
            {
                sb.Append("<br/><b>STALE</b>: no recent sample, the position may be out of date.");
            }

            sb.Append("</p>");
        }
        else if (state.IsStale)
        {
            sb.Append("<p><b>STALE</b>: no recent sample, the position may be out of date.</p>");
        }

        return sb.ToString();
    }

    /// <summary>Two corner points whose bounding box is the model Region around the sample (clamped to valid ranges).</summary>
    private static IReadOnlyList<TrajectoryPoint> BoxCorners(TelemetrySample s)
    {
        var lat = s.Latitude!.Value;
        var lon = s.Longitude!.Value;
        var alt = s.AltitudeMeters ?? 0;
        TrajectoryPoint Corner(double dLat, double dLon, double dAlt, double offset) =>
            new(offset,
                Math.Clamp(lat + dLat, -90, 90),
                Math.Clamp(lon + dLon, -179.999999, 179.999999),
                alt + dAlt,
                null,
                s.Quality,
                s.SourceName);

        return
        [
            Corner(-ModelBoxHalfSpanDegrees, -ModelBoxHalfSpanDegrees, -ModelBoxHalfHeightMeters, 0),
            Corner(ModelBoxHalfSpanDegrees, ModelBoxHalfSpanDegrees, ModelBoxHalfHeightMeters, 1),
        ];
    }

    /// <summary>Last segment of the vehicle's model path; the default model file when there is no path or it ends in a slash.</summary>
    internal static string ModelFileName(Vehicle vehicle)
    {
        var last = vehicle.ModelPath?.Replace('\\', '/').Split('/')[^1].Trim();
        return string.IsNullOrEmpty(last) ? DefaultModelFile(vehicle.Type) : last;
    }

    /// <summary>File name under the model base URL when the vehicle has no model path of its own.</summary>
    public static string DefaultModelFile(VehicleType type) =>
        type == VehicleType.Starship ? "starship.dae" : "superheavy.dae";
}
