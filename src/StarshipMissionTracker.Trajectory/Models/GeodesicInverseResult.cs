namespace StarshipMissionTracker.Trajectory.Models;

/// <summary>
/// Solution of the inverse geodesic problem on the WGS84 ellipsoid.
/// Azimuths are degrees clockwise from true north (same convention as KML heading), normalised to [0, 360).
/// </summary>
/// <param name="DistanceMeters">Ellipsoidal (ground) distance along the geodesic, metres.</param>
/// <param name="InitialAzimuthDegrees">Forward azimuth at the start point.</param>
/// <param name="FinalAzimuthDegrees">Forward azimuth at the end point (direction of travel on arrival).</param>
public readonly record struct GeodesicInverseResult(
    double DistanceMeters,
    double InitialAzimuthDegrees,
    double FinalAzimuthDegrees);
