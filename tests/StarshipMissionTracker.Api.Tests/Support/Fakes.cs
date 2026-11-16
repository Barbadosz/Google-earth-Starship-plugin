using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Kml.Dynamic;

namespace StarshipMissionTracker.Api.Tests.Support;

/// <summary>In-memory mission store; counts planned-mission loads and records T0 edits.</summary>
public sealed class FakeMissionRepository : IMissionRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, MissionInfo> _infos = new(StringComparer.Ordinal);

    public int PlannedLoads { get; private set; }
    public List<(string Code, DateTimeOffset Planned)> PlannedEdits { get; } = [];
    public string? CurrentCode { get; set; }

    /// <summary>The next <see cref="SetPlannedLaunchTimeAsync"/> fails with this exception (a storage error).</summary>
    public Exception? FailNextPlannedEdit { get; set; }

    public void Put(MissionInfo info)
    {
        lock (_gate)
        {
            _infos[info.MissionCode] = info;
            CurrentCode ??= info.MissionCode;
        }
    }

    public Task<IReadOnlyList<MissionInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MissionInfo>>(_infos.Values.OrderBy(i => i.MissionCode, StringComparer.Ordinal).ToList());
        }
    }

    public Task<MissionInfo?> GetInfoAsync(string missionCode, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_infos.GetValueOrDefault(missionCode));
        }
    }

    public Task<MissionInfo?> GetCurrentInfoAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(CurrentCode is null ? null : _infos.GetValueOrDefault(CurrentCode));
        }
    }

    public Task<Mission?> GetPlannedMissionAsync(string missionCode, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            PlannedLoads++;
            return Task.FromResult(_infos.TryGetValue(missionCode, out var info) ? TestData.Mission(info) : null);
        }
    }

    public Task<bool> ImportAsync(Mission mission, bool isSimulated, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> SetPlannedLaunchTimeAsync(string missionCode, DateTimeOffset plannedLaunchTimeUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (FailNextPlannedEdit is { } failure)
            {
                FailNextPlannedEdit = null;
                return Task.FromException<bool>(failure);
            }

            if (!_infos.TryGetValue(missionCode, out var old))
            {
                return Task.FromResult(false);
            }

            PlannedEdits.Add((missionCode, plannedLaunchTimeUtc));
            _infos[missionCode] = TestData.Info(missionCode, plannedLaunchTimeUtc, anchor: null, updated: old.UpdatedAtUtc.AddMinutes(1), old.IsSimulated);
            return Task.FromResult(true);
        }
    }

    public Task<bool> BeginSimulationRunAsync(string missionCode, DateTimeOffset simulationLaunchTimeUtc, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> SetFlightStateAsync(
        string missionCode,
        MissionStatus status,
        DateTimeOffset? actualLaunchTimeUtc,
        DateTimeOffset? simulationLaunchTimeUtc,
        bool clearActualLaunchTime = false,
        bool clearSimulationLaunchTime = false,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>Telemetry store with fixed latest samples and history paths; records the query arguments.</summary>
public sealed class FakeTelemetryRepository : ITelemetryRepository
{
    public List<TelemetrySample> Latest { get; } = [];
    public Dictionary<VehicleType, FlightPath> History { get; } = [];
    public List<int> HistoryMaxPoints { get; } = [];
    public (string Code, VehicleType? Vehicle, DateTimeOffset? Since, int Limit)? LastSamplesQuery { get; private set; }

    public Task AddRangeAsync(string missionCode, IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<TelemetrySample>> GetLatestAsync(string missionCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TelemetrySample>>(Latest.ToList());

    public Task<FlightPath?> GetHistoryAsync(string missionCode, VehicleType vehicle, int maxPoints, CancellationToken cancellationToken = default)
    {
        HistoryMaxPoints.Add(maxPoints);
        return Task.FromResult(History.GetValueOrDefault(vehicle));
    }

    public Task<IReadOnlyList<TelemetrySample>> GetSamplesAsync(
        string missionCode, VehicleType? vehicle, DateTimeOffset? since, int limit, CancellationToken cancellationToken = default)
    {
        LastSamplesQuery = (missionCode, vehicle, since, limit);
        return Task.FromResult<IReadOnlyList<TelemetrySample>>(Latest.Where(s => vehicle is null || s.Vehicle == vehicle).Take(limit).ToList());
    }

    public Task<int> DeleteSimulatedAsync(string missionCode, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// Scriptable simulation control: returns <see cref="Status"/>, or throws <see cref="NextError"/> once. Start and
/// <see cref="RunWhileInactiveAsync{T}"/> share one lock like the real controller; <see cref="HoldInactive"/> keeps the
/// guarded action waiting (inside the lock) so a test can send a concurrent Start.
/// </summary>
public sealed class FakeSimulationControl : ISimulationControl
{
    private readonly SemaphoreSlim _opLock = new(1, 1);
    private readonly Lock _callsGate = new();

    public SimulationStatus Status { get; set; } = new(SimulationState.Idle, null, 10, false, null, null, null);
    public Exception? NextError { get; set; }
    public List<string> Calls { get; } = [];
    public SimulationStartRequest? LastStart { get; private set; }
    public double? LastSpeed { get; private set; }

    /// <summary>When set, the guarded action waits for it (after <see cref="InactiveEntered"/> is signalled).</summary>
    public TaskCompletionSource? HoldInactive { get; set; }

    public TaskCompletionSource InactiveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Signalled when a Start call is waiting for (or holding) the operation lock.</summary>
    public TaskCompletionSource StartRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SimulationStatus GetStatus() => Status;

    public async Task<SimulationStatus> StartAsync(SimulationStartRequest request, CancellationToken cancellationToken = default)
    {
        StartRequested.TrySetResult();
        await _opLock.WaitAsync(cancellationToken);
        try
        {
            LastStart = request;
            return await Run("start", () => Status with
            {
                State = SimulationState.Countdown, MissionCode = request.MissionCode, Speed = request.Speed,
                CurrentOffsetSeconds = -request.CountdownSeconds, LaunchTimeUtc = TestData.PlannedT0,
            });
        }
        finally
        {
            _opLock.Release();
        }
    }

    public async Task<T> RunWhileInactiveAsync<T>(string missionCode, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        await _opLock.WaitAsync(cancellationToken);
        try
        {
            AddCall("inactive");
            if (Status.State is SimulationState.Countdown or SimulationState.Running
                && string.Equals(Status.MissionCode, missionCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new SimulationStateException(
                    $"A simulation of '{missionCode}' is {(Status.IsPaused ? "paused" : Status.State.ToString())}; stop it before changing the mission");
            }

            InactiveEntered.TrySetResult();
            if (HoldInactive is { } hold)
            {
                await hold.Task.WaitAsync(cancellationToken);
            }

            var result = await action(cancellationToken);
            AddCall("inactive-done");
            return result;
        }
        finally
        {
            _opLock.Release();
        }
    }

    private void AddCall(string name)
    {
        lock (_callsGate)
        {
            Calls.Add(name);
        }
    }

    public Task<SimulationStatus> StopAsync(CancellationToken cancellationToken = default) =>
        Run("stop", () => Status with { State = SimulationState.Idle, IsPaused = false });

    public Task<SimulationStatus> SetSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        LastSpeed = speed;
        return Run("speed", () => Status with { Speed = speed });
    }

    public Task<SimulationStatus> PauseAsync(CancellationToken cancellationToken = default) =>
        Run("pause", () => Status with { IsPaused = true });

    public Task<SimulationStatus> ResumeAsync(CancellationToken cancellationToken = default) =>
        Run("resume", () => Status with { IsPaused = false });

    private Task<SimulationStatus> Run(string name, Func<SimulationStatus> next)
    {
        AddCall(name);
        if (NextError is { } error)
        {
            NextError = null;
            return Task.FromException<SimulationStatus>(error);
        }

        Status = next();
        return Task.FromResult(Status);
    }
}

/// <summary>Real <see cref="DynamicKmlBuilder"/> that also records the arguments of each call.</summary>
public sealed class RecordingKmlBuilder : IDynamicKmlBuilder
{
    private readonly DynamicKmlBuilder _inner = new();

    public KmlEndpointUrls? MasterUrls { get; private set; }
    public KmlAssetUrls? Assets { get; private set; }
    public MissionInfo? LastInfo { get; private set; }
    public Mission? PlannedMission { get; private set; }
    public string? ControlUrl { get; private set; }
    public IReadOnlyList<Vehicle>? HistoryVehicles { get; private set; }
    public IReadOnlyList<LiveVehicleState>? LiveStates { get; private set; }
    public DateTimeOffset? LiveNow { get; private set; }
    public TimeSpan? MinRefresh { get; private set; }

    public string BuildMaster(MissionInfo info, KmlEndpointUrls urls, NetworkLinkSettings settings, KmlAssetUrls? assets = null)
    {
        (LastInfo, MasterUrls, Assets) = (info, urls, assets);
        return _inner.BuildMaster(info, urls, settings, assets);
    }

    public string BuildPlanned(
        MissionInfo info, Mission mission, IReadOnlySet<FlightPath> densified, KmlAssetUrls assets, long revision, TimeSpan minRefresh, string? controlUrl = null)
    {
        (LastInfo, PlannedMission, Assets, ControlUrl, MinRefresh) = (info, mission, assets, controlUrl, minRefresh);
        return _inner.BuildPlanned(info, mission, densified, assets, revision, minRefresh, controlUrl);
    }

    public string BuildHistory(MissionInfo info, IReadOnlyList<Vehicle> vehiclesWithActualPath, KmlAssetUrls assets, long revision, TimeSpan minRefresh)
    {
        (LastInfo, HistoryVehicles, Assets, MinRefresh) = (info, vehiclesWithActualPath, assets, minRefresh);
        return _inner.BuildHistory(info, vehiclesWithActualPath, assets, revision, minRefresh);
    }

    public string BuildLive(
        MissionInfo info, IReadOnlyList<LiveVehicleState> vehicles, DateTimeOffset nowUtc, KmlAssetUrls assets, long revision, TimeSpan minRefresh)
    {
        (LastInfo, LiveStates, LiveNow, Assets, MinRefresh) = (info, vehicles, nowUtc, assets, minRefresh);
        return _inner.BuildLive(info, vehicles, nowUtc, assets, revision, minRefresh);
    }
}
