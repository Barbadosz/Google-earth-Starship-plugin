using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Interfaces;

/// <summary>Persistence of telemetry samples. The actual track is built from these rows (not stored twice).</summary>
public interface ITelemetryRepository
{
    /// <summary>Stores the batch in one transaction.</summary>
    Task AddRangeAsync(string missionCode, IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken = default);

    /// <summary>The latest sample per vehicle (vehicles without a sample are omitted).</summary>
    Task<IReadOnlyList<TelemetrySample>> GetLatestAsync(string missionCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// The actual path of a vehicle from its position samples, decimated to at most <paramref name="maxPoints"/>
    /// (first and last kept, nothing inserted); quality = worst sample quality; null when there are no positions.
    /// </summary>
    Task<FlightPath?> GetHistoryAsync(string missionCode, VehicleType vehicle, int maxPoints, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TelemetrySample>> GetSamplesAsync(
        string missionCode,
        VehicleType? vehicle,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the samples with quality Simulated; returns the number of deleted rows.</summary>
    Task<int> DeleteSimulatedAsync(string missionCode, CancellationToken cancellationToken = default);
}
