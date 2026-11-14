namespace StarshipMissionTracker.Core.Enums;

/// <summary>Provenance/reliability of a data point (spec section 11). Simulated, Estimated and Reconstructed must never be shown as measured.</summary>
public enum DataQuality
{
    Unknown,
    Simulated,
    Estimated,
    Reconstructed,
    Published,
    Measured
}
