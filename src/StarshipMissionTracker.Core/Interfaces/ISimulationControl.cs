using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Interfaces;

/// <summary>
/// Controls the telemetry simulation. Invalid arguments throw <see cref="ArgumentOutOfRangeException"/> (400 in the API);
/// calls that do not fit the current state throw <see cref="SimulationStateException"/> (409); an unknown mission throws
/// <see cref="KeyNotFoundException"/> (404).
/// </summary>
public interface ISimulationControl
{
    Task<SimulationStatus> StartAsync(SimulationStartRequest request, CancellationToken cancellationToken = default);

    Task<SimulationStatus> StopAsync(CancellationToken cancellationToken = default);

    SimulationStatus GetStatus();

    /// <summary>Changes the replay speed (see <see cref="SimulationSpeedLimits"/>); allowed in Countdown and Running.</summary>
    Task<SimulationStatus> SetSpeedAsync(double speed, CancellationToken cancellationToken = default);

    /// <summary>Pauses a running simulation (not allowed in the countdown).</summary>
    Task<SimulationStatus> PauseAsync(CancellationToken cancellationToken = default);

    Task<SimulationStatus> ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="action"/> serialised with every other simulation operation (start, stop, ticks), but only
    /// while no run of <paramref name="missionCode"/> is active (Countdown or Running, paused included). Used for
    /// changes that must not race with a start, e.g. the planned T0 edit. The action gets <paramref name="cancellationToken"/>.
    /// The lock is not reentrant: <paramref name="action"/> must not call back into <see cref="ISimulationControl"/>
    /// (start, stop, speed, pause, resume or this method), or it deadlocks. Keep it to repository work.
    /// </summary>
    /// <exception cref="SimulationStateException">A run of that mission is active; the action is not called.</exception>
    Task<T> RunWhileInactiveAsync<T>(string missionCode, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}
