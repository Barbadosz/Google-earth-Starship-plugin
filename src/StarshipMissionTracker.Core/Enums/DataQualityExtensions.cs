namespace StarshipMissionTracker.Core.Enums;

/// <summary>
/// Explicit reliability ordering of <see cref="DataQuality"/> (worst to best):
/// Unknown &lt; Simulated &lt; Estimated &lt; Reconstructed &lt; Published &lt; Measured.
/// Independent of the enum's numeric values.
/// </summary>
public static class DataQualityExtensions
{
    /// <summary>Reliability rank: 0 (Unknown) .. 5 (Measured); higher is better.</summary>
    public static int Rank(this DataQuality quality) => quality switch
    {
        DataQuality.Unknown => 0,
        DataQuality.Simulated => 1,
        DataQuality.Estimated => 2,
        DataQuality.Reconstructed => 3,
        DataQuality.Published => 4,
        DataQuality.Measured => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Undefined data quality value."),
    };

    /// <summary>True if <paramref name="quality"/> is strictly more reliable than <paramref name="other"/>.</summary>
    public static bool IsBetterThan(this DataQuality quality, DataQuality other) => quality.Rank() > other.Rank();

    /// <summary>The less reliable of the two.</summary>
    public static DataQuality Worst(DataQuality a, DataQuality b) => a.Rank() <= b.Rank() ? a : b;
}
