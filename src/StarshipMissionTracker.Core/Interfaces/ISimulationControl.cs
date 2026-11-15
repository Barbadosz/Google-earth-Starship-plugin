using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Interfaces;

/// <summary>
/// Controls the telemetry simulation. Invalid arguments throw <see cref="ArgumentOutOfRangeException"/> (400 in the API);
/// calls that do not fit the current state throw <see cref="InvalidOperationException"/> (409).
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
}
