using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Validation;

namespace StarshipMissionTracker.Telemetry.Simulation;

/// <summary>
/// In-memory simulation state machine (Idle, Countdown, Running, Completed) behind <see cref="ISimulationControl"/>.
/// Singleton: repositories come from a fresh scope per operation. Locking: <c>_opLock</c> serialises every operation and
/// tick including their database calls; <c>_stateLock</c> guards the state fields and is held for every change of
/// them, so the synchronous <see cref="GetStatus"/> never waits for the database and always sees a consistent snapshot.
/// Log calls made while the state changes are deferred and written after <c>_stateLock</c> is released.
/// Errors per the contract: bad speed -> <see cref="ArgumentOutOfRangeException"/>, wrong state ->
/// <see cref="InvalidOperationException"/>, unknown mission -> <see cref="KeyNotFoundException"/>.
/// </summary>
public sealed class SimulationController : ISimulationControl
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SimulationOptions _options;
    private readonly ILogger<SimulationController> _logger;

    /// <summary>Upper bound for the token-independent DB restore after a stop.</summary>
    private static readonly TimeSpan ResetTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _opLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<VehicleType, DateTimeOffset> _lastTimestamps = [];

    private SimulationState _state = SimulationState.Idle;
    private SimulationRunner? _runner;
    private double _idleSpeed;
    private string? _reason;
    private bool _pendingInFlight;
    private bool _pendingCompleted;
    private double? _finalOffsetSeconds;
    private int _failures;
    private bool _wroteSamples;

    /// <exception cref="ArgumentException">An option is out of range (no silent fallbacks).</exception>
    public SimulationController(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        SimulationOptions options,
        ILogger<SimulationController> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (!SimulationSpeedLimits.IsValid(options.DefaultSpeed))
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Simulation:DefaultSpeed must be within [{SimulationSpeedLimits.Min}, {SimulationSpeedLimits.Max}] (was {options.DefaultSpeed})."),
                nameof(options));
        }

        if (!double.IsFinite(options.SampleIntervalSeconds) || options.SampleIntervalSeconds <= 0)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Simulation:SampleIntervalSeconds must be a finite number > 0 (was {options.SampleIntervalSeconds})."),
                nameof(options));
        }

        if (options.MaxConsecutiveFailures < 1)
        {
            throw new ArgumentException(
                $"Simulation:MaxConsecutiveFailures must be >= 1 (was {options.MaxConsecutiveFailures}).", nameof(options));
        }

        _idleSpeed = options.DefaultSpeed;
    }

    /// <exception cref="KeyNotFoundException">Unknown mission code.</exception>
    /// <exception cref="InvalidOperationException">A simulation is already running, or the mission is not simulated.</exception>
    public async Task<SimulationStatus> StartAsync(SimulationStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            WithStateDo(log =>
            {
                Refresh(now, log);
                if (_state is SimulationState.Countdown or SimulationState.Running)
                {
                    Reject(log, $"Start rejected: a simulation is already {_state}; stop it first");
                }
            });

            using var scope = _scopeFactory.CreateScope();
            var missions = scope.ServiceProvider.GetRequiredService<IMissionRepository>();

            var info = await missions.GetInfoAsync(request.MissionCode, cancellationToken).ConfigureAwait(false)
                       ?? throw UnknownMission(request.MissionCode);
            if (!info.IsSimulated)
            {
                Reject($"Start rejected: mission '{request.MissionCode}' is not a simulated mission");
            }

            var mission = await missions.GetPlannedMissionAsync(request.MissionCode, cancellationToken).ConfigureAwait(false)
                          ?? throw UnknownMission(request.MissionCode);

            var wallLaunch = now + TimeSpan.FromTicks((long)Math.Round(request.CountdownSeconds * TimeSpan.TicksPerSecond));
            var runner = new SimulationRunner(mission, wallLaunch, request.Speed, _options.SampleIntervalSeconds);

            // The planned T0 is never written here: the run gets its own anchor (SimulationLaunchTimeUtc), which is the
            // time axis of the samples it stores. One repository transaction sets Scheduled + the anchor, clears a stale
            // ActualLaunchTimeUtc (e.g. left by a crash) and deletes the old simulated samples. If it throws, nothing was
            // changed (a previous Completed result stays intact) and the controller keeps its state.
            var found = await missions.BeginSimulationRunAsync(request.MissionCode, wallLaunch, cancellationToken).ConfigureAwait(false);
            if (!found)
            {
                _logger.LogWarning("Simulation run was not stored: mission {MissionCode} is unknown to the repository", request.MissionCode);
            }

            WithStateDo(log =>
            {
                _runner = runner;
                _state = SimulationState.Countdown;
                _reason = null;
                _pendingInFlight = false;
                _pendingCompleted = false;
                _finalOffsetSeconds = null;
                _failures = 0;
                _wroteSamples = false;
                _lastTimestamps.Clear();
                _idleSpeed = request.Speed;
                Refresh(now, log);
            });

            _logger.LogInformation(
                "Simulation started: mission {MissionCode}, speed {Speed}x, countdown {Countdown} s, T0 {LaunchTimeUtc:O}",
                request.MissionCode, request.Speed, request.CountdownSeconds, wallLaunch);
            return GetStatus();
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <exception cref="InvalidOperationException">No simulation to stop.</exception>
    public async Task<SimulationStatus> StopAsync(CancellationToken cancellationToken = default)
    {
        await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (missionCode, previous, anchor, wrote) = WithState(log =>
            {
                Refresh(_timeProvider.GetUtcNow(), log);
                if (_state == SimulationState.Idle)
                {
                    Reject(log, "Stop rejected: no simulation to stop");
                }

                var result = (_runner?.MissionCode, _state, _runner?.Clock.WallLaunchUtc, _wroteSamples);
                _idleSpeed = _runner?.Clock.Speed ?? _idleSpeed;
                ResetRun();
                return result;
            });

            _logger.LogInformation(
                "Simulation stopped: mission {MissionCode}, was {PreviousState}, simulation anchor {SimulationLaunchTimeUtc:O}", missionCode, previous, anchor);

            // Completed keeps its stored result; an interrupted run goes back to Scheduled without an actual launch time.
            // The anchor stays when samples were written (they keep their time axis until the next Start replaces both);
            // without samples it is cleared so the planned KML falls back to the planned T0.
            if (previous is SimulationState.Countdown or SimulationState.Running && missionCode is not null)
            {
                await TryResetStatusAsync(missionCode, null, clearAnchor: !wrote).ConfigureAwait(false);
            }

            return GetStatus();
        }
        finally
        {
            _opLock.Release();
        }
    }

    public SimulationStatus GetStatus()
    {
        var now = _timeProvider.GetUtcNow();
        return WithState(log =>
        {
            Refresh(now, log);
            return BuildStatus(now);
        });
    }

    /// <exception cref="ArgumentOutOfRangeException">Speed outside <see cref="SimulationSpeedLimits"/>.</exception>
    /// <exception cref="InvalidOperationException">Not in Countdown or Running.</exception>
    public async Task<SimulationStatus> SetSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        if (!SimulationSpeedLimits.IsValid(speed))
        {
            _logger.LogWarning("Speed change rejected: {Speed} is not within [{Min}, {Max}]", speed, SimulationSpeedLimits.Min, SimulationSpeedLimits.Max);
            throw new ArgumentOutOfRangeException(
                nameof(speed),
                speed,
                string.Create(CultureInfo.InvariantCulture, $"Speed must be finite and within [{SimulationSpeedLimits.Min}, {SimulationSpeedLimits.Max}]."));
        }

        await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            return WithState(log =>
            {
                Refresh(now, log);
                var runner = _runner;
                if (_state is not (SimulationState.Countdown or SimulationState.Running) || runner is null)
                {
                    Reject(log, $"Speed change rejected: the simulation is {_state}");
                }

                var old = runner.Clock.Speed;
                var offset = runner.Clock.OffsetAt(now);
                var state = _state;
                runner.Clock.SetSpeed(now, speed);
                _idleSpeed = speed;
                log.Add(() => _logger.LogInformation(
                    "Speed changed {OldSpeed}x -> {NewSpeed}x at T{Offset:+0.###;-0.###} s ({State})", old, speed, offset, state));
                return BuildStatus(now);
            });
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <exception cref="InvalidOperationException">Not running, or already paused.</exception>
    public async Task<SimulationStatus> PauseAsync(CancellationToken cancellationToken = default)
    {
        await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            return WithState(log =>
            {
                Refresh(now, log);
                var runner = _runner;
                if (_state != SimulationState.Running || runner is null)
                {
                    Reject(log, $"Pause rejected: the simulation is {_state} (pause is only allowed while running)");
                }

                if (runner.Clock.IsPaused)
                {
                    Reject(log, "Pause rejected: the simulation is already paused");
                }

                runner.Clock.Pause(now);
                var offset = runner.Clock.OffsetAt(now);
                log.Add(() => _logger.LogInformation("Simulation paused at T{Offset:+0.###;-0.###} s", offset));
                return BuildStatus(now);
            });
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <exception cref="InvalidOperationException">Not running, or not paused.</exception>
    public async Task<SimulationStatus> ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            return WithState(log =>
            {
                Refresh(now, log);
                var runner = _runner;
                if (_state != SimulationState.Running || runner is null || !runner.Clock.IsPaused)
                {
                    Reject(log, $"Resume rejected: the simulation is {_state} and not paused");
                }

                runner.Clock.Resume(now);
                var offset = runner.Clock.OffsetAt(now);
                log.Add(() => _logger.LogInformation("Simulation resumed at T{Offset:+0.###;-0.###} s", offset));
                return BuildStatus(now);
            });
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <summary>
    /// One simulator step: promotes Countdown to Running at liftoff, emits all due samples (catch-up) in one batch and
    /// completes the run. The steps are separately retryable: the InFlight and Completed status writes are remembered
    /// (<c>_pendingInFlight</c>, <c>_pendingCompleted</c>) and retried on later ticks; the sampler is rolled back only
    /// when the sample insert itself failed. Too many failures in a row stop the run.
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        await _opLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var current = WithState<(SimulationRunner? Runner, bool InFlight, bool Completed)>(log =>
            {
                Refresh(now, log);
                return _state == SimulationState.Running ? (_runner, _pendingInFlight, _pendingCompleted) : (null, false, false);
            });
            if (current.Runner is not { } runner)
            {
                return;
            }

            var snapshot = runner.Snapshot();
            var inserted = false;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var missions = scope.ServiceProvider.GetRequiredService<IMissionRepository>();
                var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
                var launch = runner.Clock.WallLaunchUtc;

                if (current.InFlight)
                {
                    await SetStateAsync(missions, runner.MissionCode, MissionStatus.InFlight, launch, null, clearActual: false, clearAnchor: false, cancellationToken).ConfigureAwait(false);
                    WithStateDo(_ => _pendingInFlight = false);
                }

                if (!current.Completed)
                {
                    var batch = runner.Advance(now);
                    var accepted = Validate(batch, now, runner.MissionCode, out var lastTimestamps);
                    if (accepted.Count > 0)
                    {
                        await telemetry.AddRangeAsync(runner.MissionCode, accepted, cancellationToken).ConfigureAwait(false);
                    }

                    // The samples are stored: from here on the sampler position and the last timestamps are final, whatever
                    // the status write below does (otherwise a retry would insert the same samples again).
                    inserted = true;
                    WithStateDo(_ =>
                    {
                        _wroteSamples |= accepted.Count > 0;
                        foreach (var (vehicle, timestamp) in lastTimestamps)
                        {
                            _lastTimestamps[vehicle] = timestamp;
                        }

                        if (runner.IsComplete)
                        {
                            _pendingCompleted = true;
                            _finalOffsetSeconds = runner.Clock.OffsetAt(now);
                        }
                    });

                    _logger.LogDebug(
                        "Tick: mission {MissionCode}, offset {Offset:0.###} s, samples written {Count}",
                        runner.MissionCode, runner.Clock.OffsetAt(now), accepted.Count);
                }

                var completePending = WithState(_ => _pendingCompleted);
                if (completePending)
                {
                    await SetStateAsync(missions, runner.MissionCode, MissionStatus.Completed, launch, null, clearActual: false, clearAnchor: false, cancellationToken).ConfigureAwait(false);
                    var finalOffset = WithState(_ =>
                    {
                        _pendingCompleted = false;
                        _state = SimulationState.Completed;
                        return _finalOffsetSeconds;
                    });
                    _logger.LogInformation(
                        "Simulation completed: mission {MissionCode}, final offset {Offset:0.###} s", runner.MissionCode, finalOffset);
                }

                WithStateDo(_ => _failures = 0);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                RollBackIfNotInserted(runner, snapshot, inserted);
                throw;
            }
            catch (Exception ex)
            {
                RollBackIfNotInserted(runner, snapshot, inserted);
                await HandleTickFailureAsync(runner, now, ex).ConfigureAwait(false);
            }
        }
        finally
        {
            _opLock.Release();
        }
    }

    private static void RollBackIfNotInserted(SimulationRunner runner, PathReplaySampler.SamplerState snapshot, bool inserted)
    {
        if (!inserted)
        {
            runner.Restore(snapshot);
        }
    }

    private List<TelemetrySample> Validate(
        IReadOnlyList<TelemetrySample> batch, DateTimeOffset now, string missionCode, out Dictionary<VehicleType, DateTimeOffset> last)
    {
        var previous = WithState(_ => new Dictionary<VehicleType, DateTimeOffset>(_lastTimestamps));

        var accepted = new List<TelemetrySample>(batch.Count);
        foreach (var sample in batch)
        {
            DateTimeOffset? prior = previous.TryGetValue(sample.Vehicle, out var p) ? p : null;
            var result = TelemetryValidator.Validate(sample, prior, now);
            if (!result.IsValid)
            {
                _logger.LogWarning(
                    "Rejected sample: mission {MissionCode}, vehicle {Vehicle}, timestamp {Timestamp:O}: {Reason}",
                    missionCode, sample.Vehicle, sample.TimestampUtc, result.Reason);
                continue;
            }

            accepted.Add(sample);
            previous[sample.Vehicle] = sample.TimestampUtc;
        }

        last = previous;
        return accepted;
    }

    private async Task HandleTickFailureAsync(SimulationRunner runner, DateTimeOffset now, Exception ex)
    {
        var (failures, stopped, wrote) = WithState(_ =>
        {
            var count = ++_failures;
            var stop = count >= _options.MaxConsecutiveFailures && _runner == runner;
            var wroteSamples = _wroteSamples;
            if (stop)
            {
                ResetRun();
                _reason = "Stopped after repeated storage errors";
            }

            return (count, stop, wroteSamples);
        });

        // The exception detail goes to the log only, never into the status text.
        _logger.LogError(
            ex,
            "Simulation tick failed: mission {MissionCode}, offset {Offset:0.###} s, consecutive failures {Failures}{Stopped}",
            runner.MissionCode, runner.Clock.OffsetAt(now), failures, stopped ? " (run stopped)" : string.Empty);

        if (stopped)
        {
            await TryResetStatusAsync(runner.MissionCode, null, clearAnchor: !wrote).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Best effort: Scheduled without an actual launch time; the simulation anchor is set to <paramref name="anchor"/>,
    /// cleared (<paramref name="clearAnchor"/>) or, when both are unset, left unchanged. The planned T0 is never touched.
    /// A failure or timeout is logged, never thrown. Deliberately independent of any caller token (a cancelled Stop must
    /// still reset the DB) but bounded by <see cref="ResetTimeout"/> so host shutdown cannot hang.
    /// </summary>
    private async Task TryResetStatusAsync(string missionCode, DateTimeOffset? anchor, bool clearAnchor)
    {
        try
        {
            using var cts = new CancellationTokenSource(ResetTimeout);
            using var scope = _scopeFactory.CreateScope();
            var missions = scope.ServiceProvider.GetRequiredService<IMissionRepository>();
            await SetStateAsync(missions, missionCode, MissionStatus.Scheduled, null, anchor, clearActual: true, clearAnchor, cts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reset the status of mission {MissionCode} after stop", missionCode);
        }
    }

    private KeyNotFoundException UnknownMission(string missionCode)
    {
        _logger.LogWarning("Start rejected: unknown mission {MissionCode}", missionCode);
        return new KeyNotFoundException($"Unknown mission '{missionCode}'.");
    }

    /// <summary>
    /// Stores the flight state. A repository answer of "mission not found" is only logged as a warning, not turned
    /// into an error (the mission was just read, so this means it was deleted concurrently).
    /// </summary>
    private async Task SetStateAsync(
        IMissionRepository missions,
        string missionCode,
        MissionStatus status,
        DateTimeOffset? actualLaunchTimeUtc,
        DateTimeOffset? simulationLaunchTimeUtc,
        bool clearActual,
        bool clearAnchor,
        CancellationToken cancellationToken)
    {
        var found = await missions.SetFlightStateAsync(
                missionCode, status, actualLaunchTimeUtc, simulationLaunchTimeUtc, clearActual, clearAnchor, cancellationToken)
            .ConfigureAwait(false);
        if (!found)
        {
            _logger.LogWarning("Status {Status} was not stored: mission {MissionCode} is unknown to the repository", status, missionCode);
        }
    }

    /// <summary>Runs <paramref name="body"/> under <c>_stateLock</c>; the log calls it queued are written after the lock is released.</summary>
    private T WithState<T>(Func<List<Action>, T> body)
    {
        var deferred = new List<Action>();
        try
        {
            lock (_stateLock)
            {
                return body(deferred);
            }
        }
        finally
        {
            foreach (var log in deferred)
            {
                log();
            }
        }
    }

    private void WithStateDo(Action<List<Action>> body) => WithState<object?>(log =>
    {
        body(log);
        return null;
    });

    /// <summary>Back to Idle without a run. Caller holds <c>_stateLock</c>.</summary>
    private void ResetRun()
    {
        _runner = null;
        _state = SimulationState.Idle;
        _reason = null;
        _pendingInFlight = false;
        _pendingCompleted = false;
        _finalOffsetSeconds = null;
        _wroteSamples = false;
    }

    /// <summary>Countdown -> Running at liftoff (wall clock). Caller holds <c>_stateLock</c>.</summary>
    private void Refresh(DateTimeOffset now, List<Action> log)
    {
        if (_state == SimulationState.Countdown && _runner is { } runner && runner.Clock.HasLiftedOff(now))
        {
            _state = SimulationState.Running;
            _pendingInFlight = true;
            log.Add(() => _logger.LogInformation(
                "Liftoff: mission {MissionCode}, T0 {LaunchTimeUtc:O}", runner.MissionCode, runner.Clock.WallLaunchUtc));
        }
    }

    /// <summary>Caller holds <c>_stateLock</c>.</summary>
    private SimulationStatus BuildStatus(DateTimeOffset now)
    {
        if (_runner is not { } runner)
        {
            return new SimulationStatus(_state, null, _idleSpeed, false, null, null, _reason);
        }

        var offset = _state == SimulationState.Completed && _finalOffsetSeconds is { } final ? final : runner.Clock.OffsetAt(now);
        return new SimulationStatus(
            _state, runner.MissionCode, runner.Clock.Speed, runner.Clock.IsPaused, runner.Clock.WallLaunchUtc, offset, _reason);
    }

    /// <summary>Queues the warning (written after the lock is released) and throws. Caller holds <c>_stateLock</c>.</summary>
    [DoesNotReturn]
    private void Reject(List<Action> log, string message)
    {
        log.Add(() => _logger.LogWarning("{Message}", message));
        throw new InvalidOperationException(message);
    }

    /// <summary>Rejects outside the state lock.</summary>
    [DoesNotReturn]
    private void Reject(string message)
    {
        _logger.LogWarning("{Message}", message);
        throw new InvalidOperationException(message);
    }
}
