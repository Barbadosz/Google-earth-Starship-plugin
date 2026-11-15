namespace StarshipMissionTracker.Core.Models;

/// <summary>A vehicle with its latest telemetry sample (null = none yet), the sample age and the stale flag.</summary>
public sealed record LiveVehicleState(Vehicle Vehicle, TelemetrySample? Latest, TimeSpan? Age, bool IsStale);
