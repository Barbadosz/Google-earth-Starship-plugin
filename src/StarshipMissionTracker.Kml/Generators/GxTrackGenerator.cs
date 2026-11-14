using System.Xml.Linq;
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
        var ms = NeedsMilliseconds(path.Points.Select(p => p.OffsetSeconds));

        return K("Placemark",
            K("name", $"{vehicle.Name} – {KmlPathInfo.Kind(path)} track"),
            K("description", KmlPathInfo.Description(path)),
            Span(path, clock, ms),
            K("styleUrl", "#" + KmlLod.PathStyleId(path.Type, vehicle.Type, path.Quality)),
            KmlPathInfo.Extended(vehicle, path),
            CreateTrack(path, clock, model: null));
    }

    /// <summary>TimeSpan from the first to the last point of the path.</summary>
    internal static XElement Span(FlightPath path, LaunchClock clock, bool milliseconds) =>
        TimeSpan(clock, path.Points[0].OffsetSeconds, path.Points[^1].OffsetSeconds, milliseconds);

    /// <summary>The gx:Track element (altitudeMode, when*, gx:coord*, gx:angles*, Model).</summary>
    internal static XElement CreateTrack(FlightPath path, LaunchClock clock, XElement? model)
    {
        if (path.Points.Count == 0)
        {
            throw new ArgumentException("The flight path has no points.", nameof(path));
        }

        var ms = NeedsMilliseconds(path.Points.Select(p => p.OffsetSeconds));
        var track = G("Track", K("altitudeMode", "absolute"));
        foreach (var p in path.Points)
        {
            track.Add(K("when", clock.Format(p.OffsetSeconds, ms)));
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
