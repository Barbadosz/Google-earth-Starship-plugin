using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Calculation;
using static StarshipMissionTracker.Trajectory.Tests.TestPoints;

namespace StarshipMissionTracker.Trajectory.Tests.Calculation;

public class FlightPathPreparerTests
{
    private static FlightPath PathOf(DataQuality quality, params TrajectoryPoint[] points) =>
        new(TrajectoryType.Planned, quality, Source, "license", points);

    [Fact]
    public void Prepare_LongSegment_InsertsPointsAndLowersQualityToWorstPoint()
    {
        var path = PathOf(DataQuality.Reconstructed, At(0, 25.99, -97.15, 0), At(300, 24.5, -88.0, 150_000));

        var prepared = new FlightPathPreparer().Prepare(path);

        Assert.True(prepared.Points.Count > path.Points.Count);
        Assert.Equal(DataQuality.Estimated, prepared.Quality);
        Assert.Equal(path.Type, prepared.Type);
        Assert.Equal(path.SourceName, prepared.SourceName);
        Assert.Equal(path.License, prepared.License);
        Assert.Equal(2, path.Points.Count);
    }

    [Fact]
    public void Prepare_NoInsertionNeeded_KeepsPointCountAndQuality()
    {
        var path = PathOf(
            DataQuality.Reconstructed,
            At(0, 25.99, -97.15, 0, heading: 10, pitch: 0, roll: 0),
            At(5, 25.9901, -97.1499, 10, heading: 10, pitch: 0, roll: 0));

        var prepared = new FlightPathPreparer().Prepare(path);

        Assert.Equal(2, prepared.Points.Count);
        Assert.Equal(DataQuality.Reconstructed, prepared.Quality);
    }

    [Fact]
    public void Prepare_MissingOrientation_IsFilled()
    {
        var path = PathOf(DataQuality.Reconstructed, At(0, 25.99, -97.15, 0), At(5, 25.9901, -97.1499, 10));

        var prepared = new FlightPathPreparer().Prepare(path);

        Assert.All(prepared.Points, p => Assert.NotNull(p.HeadingDegrees));
    }

    [Fact]
    public void Prepare_WorsePathQuality_IsKept()
    {
        var path = PathOf(DataQuality.Estimated, At(0, 25.99, -97.15, 0, DataQuality.Estimated), At(5, 25.9901, -97.1499, 10, DataQuality.Estimated));

        var prepared = new FlightPathPreparer().Prepare(path);

        Assert.Equal(DataQuality.Estimated, prepared.Quality);
    }

    [Fact]
    public void Prepare_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FlightPathPreparer().Prepare(null!));
}
