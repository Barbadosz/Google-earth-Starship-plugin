using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Trajectory.Calculation;

/// <summary>Boundary checks shared by the public Trajectory helpers that accept raw point lists.</summary>
internal static class TrajectoryPointValidation
{
    /// <summary>Throws if the list or an element is null, or offsets are not strictly increasing.</summary>
    public static void EnsureStrictlyIncreasing(IReadOnlyList<TrajectoryPoint> points, string paramName)
    {
        ArgumentNullException.ThrowIfNull(points, paramName);

        for (var i = 0; i < points.Count; i++)
        {
            if (points[i] is null)
            {
                throw new ArgumentException($"Point at index {i} is null.", paramName);
            }

            if (i > 0 && points[i].OffsetSeconds <= points[i - 1].OffsetSeconds)
            {
                throw new ArgumentException(
                    $"Offsets must be strictly increasing: index {i} has T+{points[i].OffsetSeconds} s " +
                    $"after T+{points[i - 1].OffsetSeconds} s.",
                    paramName);
            }
        }
    }
}
