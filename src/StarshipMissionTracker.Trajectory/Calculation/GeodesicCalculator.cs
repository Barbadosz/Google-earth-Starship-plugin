using GeographicLib;
using StarshipMissionTracker.Trajectory.Models;

namespace StarshipMissionTracker.Trajectory.Calculation;

/// <summary>
/// WGS84 geodesic maths (Karney's algorithms via GeographicLib.NET). All angles in degrees, lengths in metres.
/// Longitude differences are handled across the antimeridian by the library; results are normalised to [-180, 180).
/// </summary>
public static class GeodesicCalculator
{
    /// <summary>Distance and azimuths between two points (latitudes in [-90, 90]).</summary>
    public static GeodesicInverseResult Inverse(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        Geodesic.WGS84.Inverse(
            latitude1, longitude1, latitude2, longitude2,
            out double distanceMeters, out double azimuth1, out double azimuth2);

        return new GeodesicInverseResult(distanceMeters, NormalizeAzimuth(azimuth1), NormalizeAzimuth(azimuth2));
    }

    /// <summary>Point reached from a start point along the given azimuth after <paramref name="distanceMeters"/>.</summary>
    public static (double Latitude, double Longitude) Direct(
        double latitude, double longitude, double azimuthDegrees, double distanceMeters)
    {
        Geodesic.WGS84.Direct(latitude, longitude, azimuthDegrees, distanceMeters, out double lat2, out double lon2);
        return (lat2, NormalizeLongitude(lon2));
    }

    /// <summary>Maps any finite longitude to [-180, 180) (+180 becomes -180, the Core antimeridian rule).</summary>
    public static double NormalizeLongitude(double longitude)
    {
        if (!double.IsFinite(longitude))
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be finite.");
        }

        var lon = Math.IEEERemainder(longitude, 360);
        return lon >= 180 ? lon - 360 : lon;
    }

    /// <summary>Maps any finite azimuth to [0, 360).</summary>
    public static double NormalizeAzimuth(double azimuthDegrees)
    {
        var azimuth = azimuthDegrees % 360;
        if (azimuth < 0)
        {
            azimuth += 360;
        }

        return azimuth >= 360 ? 0 : azimuth;
    }
}
