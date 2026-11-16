using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace StarshipMissionTracker.Telemetry.Tests.Support;

public sealed class FakeMissionRepository : IMissionRepository
{
    public required Mission Mission { get; init; }
    public bool IsSimulated { get; init; } = true;
    public List<(MissionStatus Status, DateTimeOffset? Actual, DateTimeOffset? Planned)> StateCalls { get; } = [];

    /// <summary>Stored values, updated like the real repository: a null time argument leaves the stored value unchanged.</summary>
    public MissionStatus StoredStatus { get; private set; } = MissionStatus.Scheduled;
    public DateTimeOffset? StoredActual { get; private set; }
    public DateTimeOffset? StoredPlanned { get; private set; }

    /// <summary>Statuses whose next write throws (once each).</summary>
    public HashSet<MissionStatus> FailOnceFor { get; } = [];

    /// <summary>Statuses whose writes always throw.</summary>
    public HashSet<MissionStatus> FailAlwaysFor { get; } = [];

    /// <summary>Makes SetFlightStateAsync report an unknown mission (false) without storing.</summary>
    public bool ReturnFalse { get; set; }

    /// <summary>Runs at the start of every SetFlightStateAsync (e.g. to cancel a caller token mid-restore).</summary>
    public Action? BeforeStateWrite { get; set; }

    public MissionInfo Info => new(
        Guid.Empty, Mission.MissionCode, Mission.Name, null, StoredStatus,
        StoredPlanned ?? Mission.Clock.LaunchTimeUtc, StoredActual, IsSimulated, Mission.Clock.LaunchTimeUtc);

    public Task<IReadOnlyList<MissionInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MissionInfo>>([Info]);

    public Task<MissionInfo?> GetInfoAsync(string missionCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(missionCode == Mission.MissionCode ? Info : null);

    public Task<MissionInfo?> GetCurrentInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult<MissionInfo?>(Info);

    public Task<Mission?> GetPlannedMissionAsync(string missionCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(missionCode == Mission.MissionCode ? Mission : null);

    public Task<bool> ImportAsync(Mission mission, bool isSimulated, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<bool> SetPlannedLaunchTimeAsync(string missionCode, DateTimeOffset plannedLaunchTimeUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> SetFlightStateAsync(
        string missionCode,
        MissionStatus status,
        DateTimeOffset? actualLaunchTimeUtc,
        DateTimeOffset? plannedLaunchTimeUtc,
        CancellationToken cancellationToken = default)
    {
        BeforeStateWrite?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        StateCalls.Add((status, actualLaunchTimeUtc, plannedLaunchTimeUtc));
        if (FailAlwaysFor.Contains(status) || FailOnceFor.Remove(status))
        {
            throw new InvalidOperationException($"simulated database failure writing {status}");
        }

        if (ReturnFalse)
        {
            return Task.FromResult(false);
        }

        StoredStatus = status;
        StoredActual = actualLaunchTimeUtc ?? StoredActual;
        StoredPlanned = plannedLaunchTimeUtc ?? StoredPlanned;
        return Task.FromResult(true);
    }
}

public sealed class FakeTelemetryRepository : ITelemetryRepository
{
    public List<TelemetrySample> Samples { get; } = [];
    public int DeleteCalls { get; private set; }
    public int FailNextAdds { get; set; }
    public bool FailDelete { get; set; }

    public Task AddRangeAsync(string missionCode, IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken = default)
    {
        if (FailNextAdds > 0)
        {
            FailNextAdds--;
            throw new InvalidOperationException("simulated database failure");
        }

        Samples.AddRange(samples);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TelemetrySample>> GetLatestAsync(string missionCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TelemetrySample>>([]);

    public Task<FlightPath?> GetHistoryAsync(string missionCode, VehicleType vehicle, int maxPoints, CancellationToken cancellationToken = default) =>
        Task.FromResult<FlightPath?>(null);

    public Task<IReadOnlyList<TelemetrySample>> GetSamplesAsync(
        string missionCode, VehicleType? vehicle, DateTimeOffset? since, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TelemetrySample>>(Samples);

    public Task<int> DeleteSimulatedAsync(string missionCode, CancellationToken cancellationToken = default)
    {
        DeleteCalls++;
        if (FailDelete)
        {
            throw new InvalidOperationException("simulated database failure on delete");
        }

        var count = Samples.Count;
        Samples.Clear();
        return Task.FromResult(count);
    }
}

/// <summary>Minimal scope factory: resolves the two repository fakes (no DI container package in the test project).</summary>
public sealed class FakeScopeFactory : IServiceScopeFactory
{
    private readonly IServiceProvider _provider;

    public FakeScopeFactory(IMissionRepository missions, ITelemetryRepository telemetry)
    {
        _provider = new Provider(missions, telemetry);
    }

    public IServiceScope CreateScope() => new Scope(_provider);

    private sealed class Scope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = provider;

        public void Dispose()
        {
        }
    }

    private sealed class Provider(IMissionRepository missions, ITelemetryRepository telemetry) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IMissionRepository) ? missions :
            serviceType == typeof(ITelemetryRepository) ? telemetry : null;
    }
}
