using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Api.Options;

/// <summary>Live document settings (section "Live").</summary>
public sealed class LiveOptions
{
    public const string SectionName = "Live";

    /// <summary>
    /// A vehicle's latest sample older than this (wall clock, by its ReceivedAtUtc) is marked STALE, but only while a run
    /// of that mission is Running and not paused.
    /// </summary>
    public double StaleAfterSeconds { get; set; } = 15;

    /// <summary>
    /// Null when valid, else the reason. At the slowest speed one sample takes <c>sampleInterval / SpeedMin</c> wall
    /// seconds; the threshold must be larger, or a healthy slow run would be marked STALE.
    /// </summary>
    public string? Validate(double sampleIntervalSeconds)
    {
        if (!double.IsFinite(StaleAfterSeconds) || StaleAfterSeconds <= 0)
        {
            return $"{SectionName}:StaleAfterSeconds must be a positive finite number, got {StaleAfterSeconds}.";
        }

        var slowest = sampleIntervalSeconds / SimulationSpeedLimits.Min;
        if (!(StaleAfterSeconds > slowest))
        {
            return $"{SectionName}:StaleAfterSeconds ({StaleAfterSeconds}) must be greater than Simulation:SampleIntervalSeconds / "
                   + $"{SimulationSpeedLimits.Min} ({slowest} s of wall time per sample at the slowest speed).";
        }

        return null;
    }
}
