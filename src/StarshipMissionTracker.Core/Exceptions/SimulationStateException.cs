namespace StarshipMissionTracker.Core.Exceptions;

/// <summary>
/// A simulation call does not fit the current simulation state (e.g. start while running, stop while idle, T0 change
/// while the mission's run is active). The API maps exactly this type to 409; any other
/// <see cref="InvalidOperationException"/> is an internal error (500). Derives from
/// <see cref="InvalidOperationException"/> so callers that catch the base type keep working.
/// </summary>
public sealed class SimulationStateException : InvalidOperationException
{
    public SimulationStateException(string message)
        : base(message)
    {
    }

    public SimulationStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
