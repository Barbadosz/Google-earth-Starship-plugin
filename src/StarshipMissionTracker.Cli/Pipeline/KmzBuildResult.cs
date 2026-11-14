namespace StarshipMissionTracker.Cli.Pipeline;

/// <summary>Summary of a generated KMZ (for logging and tests).</summary>
/// <param name="LaunchTimeUtc">T0 actually used.</param>
/// <param name="EntryNames">KMZ entries: doc.kml first, then the files in packaging order.</param>
public sealed record KmzBuildResult(
    string MissionCode,
    DateTimeOffset LaunchTimeUtc,
    int VehicleCount,
    int PointCount,
    int EventCount,
    IReadOnlyList<string> EntryNames,
    long SizeBytes);
