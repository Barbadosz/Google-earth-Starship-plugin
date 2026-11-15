namespace StarshipMissionTracker.Core.Models;

/// <summary>Request to start a simulation: mission, replay speed and wall-clock countdown before liftoff.</summary>
public sealed record SimulationStartRequest
{
    public SimulationStartRequest(string missionCode, double speed, double countdownSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionCode);
        if (!SimulationSpeedLimits.IsValid(speed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed), speed, $"Speed must be finite and within [{SimulationSpeedLimits.Min}, {SimulationSpeedLimits.Max}].");
        }

        if (!double.IsFinite(countdownSeconds) || countdownSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(countdownSeconds), countdownSeconds, "Countdown must be a finite number >= 0.");
        }

        MissionCode = missionCode;
        Speed = speed;
        CountdownSeconds = countdownSeconds;
    }

    public string MissionCode { get; }
    public double Speed { get; }
    public double CountdownSeconds { get; }
}
