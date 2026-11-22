namespace StarshipMissionTracker.Telemetry.Simulation;

/// <summary>Simulator settings (section "Simulation" of the configuration).</summary>
public sealed class SimulationOptions
{
    public const string SectionName = "Simulation";

    public double DefaultSpeed { get; set; } = 10;

    /// <summary>Spacing of the samples on the mission axis (default matches the 0.5 s live refresh).</summary>
    public double SampleIntervalSeconds { get; set; } = 0.5;

    /// <summary>Wake-up period of the hosted service (wall clock).</summary>
    public int TickMilliseconds { get; set; } = 250;

    /// <summary>Consecutive failing ticks after which the run is stopped (state Idle, reason in the status).</summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    public bool AutoStart { get; set; }

    public string AutoStartMissionCode { get; set; } = string.Empty;

    public double AutoStartCountdownSeconds { get; set; } = 10;
}
