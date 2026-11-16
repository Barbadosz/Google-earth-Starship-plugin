namespace StarshipMissionTracker.Api.Options;

/// <summary>History document settings (section "History").</summary>
public sealed class HistoryOptions
{
    public const string SectionName = "History";

    /// <summary>Maximum points per vehicle in the actual LineString (decimated; first and last kept, nothing inserted).</summary>
    public int MaxPointsPerVehicle { get; set; } = 2000;

    /// <summary>Null when valid, else the reason.</summary>
    public string? Validate() =>
        MaxPointsPerVehicle is >= 2 and <= 100_000
            ? null
            : $"{SectionName}:MaxPointsPerVehicle must be in [2, 100000], got {MaxPointsPerVehicle}.";
}
