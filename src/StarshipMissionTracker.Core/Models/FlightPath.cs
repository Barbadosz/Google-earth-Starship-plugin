using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Core.Models;

/// <summary>
/// An ordered point list with its provenance. Offsets must be strictly increasing (a duplicate gx:Track
/// &lt;when&gt; is invalid KML). Rules ("never presented as measured", spec section 11):
/// <list type="bullet">
/// <item>Quality may not be better than the worst point quality (see <see cref="DataQualityExtensions.Rank"/>).</item>
/// <item>Type Planned: quality may not be Measured. Type Reconstructed: quality may not be Published or Measured.
/// Type Actual: any quality.</item>
/// </list>
/// Note: a densified path contains Estimated points, so its Quality must be at most Estimated.
/// </summary>
public sealed record FlightPath
{
    public FlightPath(
        TrajectoryType type,
        DataQuality quality,
        string sourceName,
        string? license,
        IReadOnlyList<TrajectoryPoint> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        var copy = Guard.CopyNoNulls(points, nameof(points));

        for (var i = 1; i < copy.Length; i++)
        {
            if (copy[i].OffsetSeconds <= copy[i - 1].OffsetSeconds)
            {
                throw new ArgumentException(
                    $"Point offsets must be strictly increasing; point {i} ({copy[i].OffsetSeconds}) does not follow point {i - 1} ({copy[i - 1].OffsetSeconds}).",
                    nameof(points));
            }
        }

        foreach (var point in copy)
        {
            if (quality.IsBetterThan(point.Quality))
            {
                throw new ArgumentException(
                    $"Path quality {quality} is better than the point quality {point.Quality} at offset {point.OffsetSeconds}.",
                    nameof(quality));
            }
        }

        if (type == TrajectoryType.Planned && quality == DataQuality.Measured)
        {
            throw new ArgumentException("A planned path cannot have Measured quality.", nameof(quality));
        }

        if (type == TrajectoryType.Reconstructed && quality.Rank() > DataQuality.Reconstructed.Rank())
        {
            throw new ArgumentException($"A reconstructed path cannot have {quality} quality.", nameof(quality));
        }

        Type = type;
        Quality = quality;
        SourceName = sourceName;
        License = license;
        Points = copy;
    }

    public TrajectoryType Type { get; }
    public DataQuality Quality { get; }
    public string SourceName { get; }
    public string? License { get; }
    public IReadOnlyList<TrajectoryPoint> Points { get; }
}
