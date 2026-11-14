namespace StarshipMissionTracker.Core.Models;

/// <summary>
/// Limits for densification (convention: about 10 km or 10 s). A segment between two neighbouring points is split
/// when EITHER limit is exceeded (distance &gt; MaxSpacingMeters OR time gap &gt; MaxSpacingSeconds).
/// Both values must be finite and greater than zero.
/// </summary>
public sealed record TrajectoryDensificationOptions
{
    public TrajectoryDensificationOptions(double maxSpacingMeters = 10_000, double maxSpacingSeconds = 10)
    {
        if (!double.IsFinite(maxSpacingMeters) || maxSpacingMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSpacingMeters), maxSpacingMeters, "Must be finite and > 0.");
        }

        if (!double.IsFinite(maxSpacingSeconds) || maxSpacingSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSpacingSeconds), maxSpacingSeconds, "Must be finite and > 0.");
        }

        MaxSpacingMeters = maxSpacingMeters;
        MaxSpacingSeconds = maxSpacingSeconds;
    }

    public static TrajectoryDensificationOptions Default { get; } = new();

    public double MaxSpacingMeters { get; }
    public double MaxSpacingSeconds { get; }
}
