using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Trajectory.Calculation;

/// <summary>
/// Prepares a loaded flight path for KML: densifies it, derives missing orientation and lowers the path quality
/// to the worst point quality (inserted/derived points are Estimated). Shared by the CLI pipeline and the API.
/// </summary>
public sealed class FlightPathPreparer
{
    private readonly ITrajectoryProcessor _processor;
    private readonly TrajectoryDensificationOptions _options;

    public FlightPathPreparer(ITrajectoryProcessor? processor = null, TrajectoryDensificationOptions? options = null)
    {
        _processor = processor ?? new TrajectoryProcessor();
        _options = options ?? TrajectoryDensificationOptions.Default;
    }

    /// <summary>Returns a new prepared path; the input is not modified.</summary>
    public FlightPath Prepare(FlightPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var densified = _processor.Densify(path.Points, _options);
        var oriented = OrientationCalculator.FillMissing(densified);

        // Core rule: a path may not claim better quality than its worst point.
        var quality = oriented.Select(p => p.Quality).Aggregate(path.Quality, DataQualityExtensions.Worst);
        return new FlightPath(path.Type, quality, path.SourceName, path.License, oriented);
    }
}
