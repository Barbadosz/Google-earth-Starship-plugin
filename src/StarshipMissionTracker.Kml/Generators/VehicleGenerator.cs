using System.Xml.Linq;
using StarshipMissionTracker.Core.Models;
using static StarshipMissionTracker.Kml.Generators.KmlXml;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// Moving vehicle representations on a flight path: a 3D model (only when close, via Region/Lod) and a
/// navigation icon (always visible). Both are gx:Track Placemarks in two separate Folders, so they can be switched independently.
/// </summary>
public static class VehicleGenerator
{
    /// <summary>Folder "&lt;vehicle&gt; 3D Model"; empty if the vehicle has no model path.</summary>
    public static XElement CreateModelFolder(Vehicle vehicle, FlightPath path, LaunchClock clock)
    {
        var folder = K("Folder", K("name", $"{vehicle.Name} 3D Model"));
        if (vehicle.ModelPath is not null)
        {
            folder.Add(CreateModelPlacemark(vehicle, path, clock));
        }

        return folder;
    }

    /// <summary>Folder "&lt;vehicle&gt; Icon".</summary>
    public static XElement CreateIconFolder(Vehicle vehicle, FlightPath path, LaunchClock clock) =>
        K("Folder", K("name", $"{vehicle.Name} Icon"), CreateIconPlacemark(vehicle, path, clock));

    /// <summary>The model is visible when the region is large on screen (zoomed in).</summary>
    public static XElement CreateModelPlacemark(Vehicle vehicle, FlightPath path, LaunchClock clock)
    {
        Validate(vehicle, path, clock);
        if (vehicle.ModelPath is null)
        {
            throw new ArgumentException($"Vehicle '{vehicle.Name}' has no model path.", nameof(vehicle));
        }

        // No Model/Orientation: when the track carries gx:angles they set the model's attitude, and a
        // second Orientation from the first point would rotate it twice. Without angles the KML default
        // (0, 0, 0) applies, i.e. no orientation is invented. (KML 2.2: Orientation is optional.)
        // The href is URL-escaped (spaces etc.); Core guarantees forward slashes only.
        var fileName = vehicle.ModelPath.Split('/')[^1];
        var model = K("Model",
            K("Scale", K("x", "1"), K("y", "1"), K("z", "1")),
            K("Link", K("href", "files/" + Uri.EscapeDataString(fileName))));

        return Build(vehicle, path, clock, $"{vehicle.Name} 3D Model", null, model,
            new Lod(KmlLod.ModelMinLodPixels, KmlLod.ModelMaxLodPixels));
    }

    /// <summary>The icon has no Region: it is always visible, so the vehicle never vanishes at mid zoom.</summary>
    public static XElement CreateIconPlacemark(Vehicle vehicle, FlightPath path, LaunchClock clock)
    {
        Validate(vehicle, path, clock);
        return Build(vehicle, path, clock, $"{vehicle.Name} Icon", KmlLod.IconStyleId(vehicle.Type), null, null);
    }

    private readonly record struct Lod(int Min, int Max);

    private static XElement Build(
        Vehicle vehicle, FlightPath path, LaunchClock clock, string name, string? styleId,
        XElement? model, Lod? lod)
    {
        var ms = NeedsMilliseconds(path.Points.Select(p => p.OffsetSeconds));
        var placemark = K("Placemark",
            K("name", name),
            K("description", Cdata(
                $"<p>Position along the {Html(KmlPathInfo.Kind(path))} path. {Html(KmlPathInfo.Label(path))}</p>"
                + $"<p>{Html(QualityNote(path.Quality))}<br/>Source: {Html(path.SourceName)}"
                + (KmlPathInfo.HasAngles(path) ? $"<br/>{Html(KmlPathInfo.OrientationNote)}" : string.Empty)
                + "</p>")),
            GxTrackGenerator.Span(path, clock, ms));
        if (styleId is not null)
        {
            placemark.Add(K("styleUrl", "#" + styleId));
        }

        if (lod is { } l)
        {
            placemark.Add(Region(path.Points, l.Min, l.Max));
        }

        placemark.Add(
            KmlPathInfo.Extended(vehicle, path),
            GxTrackGenerator.CreateTrack(path, clock, model));
        return placemark;
    }

    private static void Validate(Vehicle vehicle, FlightPath path, LaunchClock clock)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(clock);
        if (path.Points.Count == 0)
        {
            throw new ArgumentException("The flight path has no points.", nameof(path));
        }
    }
}
