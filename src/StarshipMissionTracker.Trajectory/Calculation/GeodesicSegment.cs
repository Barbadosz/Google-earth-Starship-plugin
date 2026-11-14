using GeographicLib;

namespace StarshipMissionTracker.Trajectory.Calculation;

/// <summary>
/// The WGS84 geodesic between two points, solved once so many positions along it can be computed cheaply.
/// Safe across the antimeridian (the library works with the longitude difference, not raw values).
/// </summary>
public sealed class GeodesicSegment
{
    private const GeodesicFlags Caps =
        GeodesicFlags.Latitude | GeodesicFlags.Longitude | GeodesicFlags.Azimuth |
        GeodesicFlags.Distance | GeodesicFlags.DistanceIn;

    private readonly IGeodesicLine _line;

    public GeodesicSegment(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        _line = Geodesic.WGS84.InverseLine(latitude1, longitude1, latitude2, longitude2, Caps);
        DistanceMeters = _line.Distance;
    }

    /// <summary>Ground distance along the ellipsoid, metres.</summary>
    public double DistanceMeters { get; }

    /// <summary>
    /// Position at <paramref name="fraction"/> (0 = start, 1 = end) of the distance; longitude in [-180, 180),
    /// azimuth (direction of travel at that point) in [0, 360).
    /// </summary>
    public (double Latitude, double Longitude, double AzimuthDegrees) PositionAt(double fraction)
    {
        if (!double.IsFinite(fraction) || fraction is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Fraction must be within [0, 1].");
        }

        _line.Position(fraction * DistanceMeters, out double lat, out double lon, out double azimuth);
        return (lat, GeodesicCalculator.NormalizeLongitude(lon), GeodesicCalculator.NormalizeAzimuth(azimuth));
    }
}
