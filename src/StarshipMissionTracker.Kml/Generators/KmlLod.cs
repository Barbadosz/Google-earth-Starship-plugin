using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Kml.Generators;

/// <summary>
/// Region/Lod thresholds, style IDs and folder names shared by the generators and the
/// document builder. The style definitions themselves live in <c>KmlStyles</c>.
/// </summary>
public static class KmlLod
{
    // Lod values are screen pixels covered by the Region (the whole flight path box).
    // Placeholder values: tune in GE Pro (N-9).
    // Only the 3D model has a Region/Lod. The icon has none, so it is always visible and the
    // vehicle can never vanish at mid zoom; the model is drawn on top when the viewer is close.

    /// <summary>The 3D model is shown once the region is at least this many pixels large (tune in GE Pro, N-9).</summary>
    public const int ModelMinLodPixels = 2000;

    /// <summary>-1 = no upper limit (tune in GE Pro, N-9).</summary>
    public const int ModelMaxLodPixels = -1;

    /// <summary>Style IDs (without the leading '#') that <c>KmlStyles</c> must define.</summary>
    public static class StyleId
    {
        public const string PlannedStarship = "planned-starship";
        public const string PlannedSuperHeavy = "planned-superheavy";
        public const string ReconstructedStarship = "reconstructed-starship";
        public const string ReconstructedSuperHeavy = "reconstructed-superheavy";
        public const string ActualStarship = "actual-starship";
        public const string ActualSuperHeavy = "actual-superheavy";

        /// <summary>Estimated/simulated parts of an Actual path: orange, never the measured green.</summary>
        public const string EstimatedStarship = "estimated-starship";
        public const string EstimatedSuperHeavy = "estimated-superheavy";

        public const string IconStarship = "icon-starship";
        public const string IconSuperHeavy = "icon-superheavy";
        public const string Event = "event";
    }

    /// <summary>Folder names, spec section 9.1.</summary>
    public static class FolderName
    {
        public const string MissionInformation = "Mission Information";
        public const string PlannedTrajectory = "Planned Trajectory";
        public const string ActualTrajectory = "Actual Trajectory";
        public const string Vehicles = "Vehicles";
        public const string MissionEvents = "Mission Events";
        public const string Starship = "Starship";
        public const string SuperHeavy = "Super Heavy";
    }

    /// <summary>Style ID (no '#') for a path/track Placemark. Estimated actual data never gets the measured style.</summary>
    public static string PathStyleId(TrajectoryType type, VehicleType vehicle, DataQuality quality)
    {
        var starship = vehicle == VehicleType.Starship;
        var estimatedActual = type == TrajectoryType.Actual && IsEstimated(quality);
        return (type, estimatedActual) switch
        {
            (_, true) => starship ? StyleId.EstimatedStarship : StyleId.EstimatedSuperHeavy,
            (TrajectoryType.Planned, _) => starship ? StyleId.PlannedStarship : StyleId.PlannedSuperHeavy,
            (TrajectoryType.Reconstructed, _) => starship ? StyleId.ReconstructedStarship : StyleId.ReconstructedSuperHeavy,
            _ => starship ? StyleId.ActualStarship : StyleId.ActualSuperHeavy,
        };
    }

    /// <summary>Style ID (no '#') of the navigation icon of a vehicle.</summary>
    public static string IconStyleId(VehicleType vehicle) =>
        vehicle == VehicleType.Starship ? StyleId.IconStarship : StyleId.IconSuperHeavy;

    internal static bool IsEstimated(DataQuality quality) =>
        quality is not (DataQuality.Published or DataQuality.Measured);
}
