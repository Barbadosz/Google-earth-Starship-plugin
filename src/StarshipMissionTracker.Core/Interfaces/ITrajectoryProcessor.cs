using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Core.Interfaces;

public interface ITrajectoryProcessor
{
    /// <summary>
    /// Great-circle densification: returns the original points plus inserted points (quality Estimated, never Measured)
    /// so that neighbours are within the options' limits. Does not mutate the input.
    /// </summary>
    IReadOnlyList<TrajectoryPoint> Densify(IReadOnlyList<TrajectoryPoint> points, TrajectoryDensificationOptions options);
}
