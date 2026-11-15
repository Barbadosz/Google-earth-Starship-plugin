using System.Globalization;
using System.Xml.Linq;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// Time-aware <c>gx:Track</c>: one <c>when</c> + <c>gx:coord</c> (lon lat alt) per point, so the
/// Google Earth time slider moves the vehicle. Also used by <see cref="VehicleGenerator"/> to put the
/// 3D model / icon on the same track.
/// </summary>
public static class GxTrackGenerator
{
    /// <summary>Trail Placemark: TimeSpan T0..last point and a gx:Track without a model.</summary>
    public static XElement CreatePlacemark(Vehicle vehicle, FlightPath path, LaunchClock clock)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(clock);

        return K("Placemark",
            K("name", $"{vehicle.Name} – {KmlPathInfo.Kind(path)} track"),
            K("description", KmlPathInfo.Description(path)),
            Span(path, clock, vehicle.Name),
            K("styleUrl", "#" + KmlLod.PathStyleId(path.Type, vehicle.Type, path.Quality)),
            KmlPathInfo.Extended(vehicle, path),
            CreateTrack(path, clock, model: null, vehicle.Name));
    }

    /// <summary>TimeSpan from the first to the last point, in the same format as the track's <c>when</c> values.</summary>
    internal static XElement Span(FlightPath path, LaunchClock clock, string? vehicleName = null)
    {
        var t = Times(path, clock, vehicleName);
        return K("TimeSpan", K("begin", t.Begin), K("end", t.End));
    }

    /// <summary>
    /// The timestamps of one track, formatted once: seconds or milliseconds as needed, and exact ticks (7 digits) for the
    /// whole track if millisecond text would repeat a <c>when</c>. Offsets less than one tick apart cannot be told apart
    /// in any format and are rejected.
    /// </summary>
    internal static (List<string> Whens, string Begin, string End) Times(
        FlightPath path, LaunchClock clock, string? vehicleName = null)
    {
        var offsets = path.Points.Select(p => p.OffsetSeconds).ToList();
        var ms = NeedsMilliseconds(offsets);
        Func<double, string> format = o => clock.Format(o, ms);
        var whens = offsets.Select(format).ToList();
        if (HasDuplicates(whens))
        {
            // Offsets closer than 1 ms would repeat a timestamp; use exact tick precision for the whole track.
            format = clock.FormatExact;
            whens = offsets.Select(format).ToList();
            if (HasDuplicates(whens))
            {
                var dup = Enumerable.Range(1, whens.Count - 1).First(i => whens.IndexOf(whens[i]) != i);
                var first = whens.IndexOf(whens[dup]);
                throw new MissionDataException(
                    $"Vehicle '{vehicleName ?? "?"}' (path source '{path.SourceName}'): offsets " +
                    $"{offsets[first].ToString("R", CultureInfo.InvariantCulture)} s and " +
                    $"{offsets[dup].ToString("R", CultureInfo.InvariantCulture)} s are less than one tick (100 ns) apart; " +
                    "KML timestamps cannot tell them apart.");
            }
        }

        return (whens, format(offsets[0]), format(offsets[^1]));
    }

    private static bool HasDuplicates(List<string> whens) =>
        whens.Distinct(StringComparer.Ordinal).Count() != whens.Count;

    /// <summary>The gx:Track element (altitudeMode, when*, gx:coord*, gx:angles*, Model).</summary>
    internal static XElement CreateTrack(FlightPath path, LaunchClock clock, XElement? model, string? vehicleName = null)
    {
        if (path.Points.Count == 0)
        {
            throw new ArgumentException("The flight path has no points.", nameof(path));
        }

        var track = G("Track", K("altitudeMode", "absolute"));
        foreach (var when in Times(path, clock, vehicleName).Whens)
        {
            track.Add(K("when", when));
        }

        foreach (var p in path.Points)
        {
            track.Add(G("coord", GxCoordinates(p)));
        }

        // All-or-nothing: a missing orientation is never written as 0 (that would show an upright model
        // on a horizontal path). Package H fills gaps with OrientationCalculator.FillMissing beforehand.
        if (KmlPathInfo.HasAngles(path))
        {
            foreach (var p in path.Points)
            {
                track.Add(G("angles", Angles(p)));
            }
        }

        if (model is not null)
        {
            track.Add(model);
        }

        return track;
    }

    /// <summary>
    /// "heading tilt roll" of a point whose three components are all known. Pitch above the horizon p
    /// becomes tilt = 90 - p (docs/assets-licenses.md).
    /// </summary>
    internal static string Angles(TrajectoryPoint p)
    {
        if (p.HeadingDegrees is not { } heading || p.PitchDegrees is not { } pitch || p.RollDegrees is not { } roll)
        {
            throw new InvalidOperationException("Angles needs heading, pitch and roll.");
        }

        return $"{Angle(heading)} {Angle(90 - pitch)} {Angle(roll)}";
    }
}
