using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Trajectory.Calculation;
using static StarshipMissionTracker.Trajectory.Tests.TestPoints;

namespace StarshipMissionTracker.Trajectory.Tests.Calculation;

public class OrientationCalculatorTests
{
    private const double EquatorDegreeMeters = 111_319.490793;

    [Fact]
    public void HeadingDegrees_EastAlongEquator_Is90()
    {
        Assert.Equal(90, OrientationCalculator.HeadingDegrees(At(0, 0, 0), At(1, 0, 1))!.Value, 1e-9);
    }

    [Fact]
    public void HeadingDegrees_DueSouth_Is180()
    {
        Assert.Equal(180, OrientationCalculator.HeadingDegrees(At(0, 10, 5), At(1, 9, 5))!.Value, 1e-9);
    }

    [Fact]
    public void HeadingDegrees_JfkToLhr_IsGeodesicAzimuth()
    {
        Assert.Equal(51.198882845, OrientationCalculator.HeadingDegrees(At(0, 40.6, -73.8), At(1, 51.6, -0.5))!.Value, 1e-6);
    }

    [Fact]
    public void HeadingDegrees_WestAcrossAntimeridian_Is270()
    {
        Assert.Equal(270, OrientationCalculator.HeadingDegrees(At(0, 0, -179.5), At(1, 0, 179.5))!.Value, 1e-9);
    }

    [Fact]
    public void PitchDegrees_ClimbEqualToGroundDistance_Is45()
    {
        var pitch = OrientationCalculator.PitchDegrees(At(0, 0, 0, 0), At(1, 0, 1, EquatorDegreeMeters));

        Assert.Equal(45, pitch!.Value, 1e-6);
    }

    [Fact]
    public void PitchDegrees_Descent_IsNegative()
    {
        var pitch = OrientationCalculator.PitchDegrees(At(0, 0, 0, EquatorDegreeMeters), At(1, 0, 1, 0));

        Assert.Equal(-45, pitch!.Value, 1e-6);
    }

    [Fact]
    public void VerticalOnlySegment_HeadingUndefinedPitch90()
    {
        var from = At(0, 25.99, -97.15, 0);
        var to = At(10, 25.99, -97.15, 1000);

        Assert.Null(OrientationCalculator.HeadingDegrees(from, to));
        Assert.Equal(90, OrientationCalculator.PitchDegrees(from, to)!.Value, 1e-9);
    }

    [Fact]
    public void ZeroLengthSegment_BothUndefined()
    {
        var from = At(0, 25.99, -97.15, 100);
        var to = At(10, 25.99, -97.15, 100);

        Assert.Null(OrientationCalculator.HeadingDegrees(from, to));
        Assert.Null(OrientationCalculator.PitchDegrees(from, to));
    }

    [Fact]
    public void FillMissing_VerticalLiftoffThenEast_LeadingHeadingTakenFromLaterSegment()
    {
        TrajectoryPoint[] input =
        [
            At(0, 0, 0, 0),
            At(10, 0, 0, 1000),
            At(20, 0, 1, 1000 + EquatorDegreeMeters),
            At(30, 0, 2, 1000 + EquatorDegreeMeters),
        ];

        var result = OrientationCalculator.FillMissing(input);

        Assert.All(result, p => Assert.Equal(90, p.HeadingDegrees!.Value, 1e-9));
        Assert.Equal(90, result[0].PitchDegrees!.Value, 1e-9);
        Assert.Equal(45, result[1].PitchDegrees!.Value, 1e-6);
        Assert.Equal(0, result[2].PitchDegrees!.Value, 1e-9);
        Assert.Equal(0, result[3].PitchDegrees!.Value, 1e-9);
        Assert.All(result, p => Assert.Equal(0, p.RollDegrees));
        Assert.All(result, p => Assert.Equal(DataQuality.Estimated, p.Quality));
    }

    [Fact]
    public void FillMissing_LastPoint_UsesArrivalAzimuth()
    {
        TrajectoryPoint[] input = [At(0, 40.6, -73.8), At(1, 51.6, -0.5)];

        var result = OrientationCalculator.FillMissing(input);

        Assert.Equal(51.198882845, result[0].HeadingDegrees!.Value, 1e-6);
        Assert.Equal(107.821776735, result[1].HeadingDegrees!.Value, 1e-6);
    }

    [Fact]
    public void FillMissing_ExistingValues_Kept()
    {
        TrajectoryPoint[] input = [At(0, 0, 0, heading: 12, pitch: 34, roll: 5), At(1, 0, 1)];

        var result = OrientationCalculator.FillMissing(input);

        Assert.Same(input[0], result[0]);
        Assert.Equal(90, result[1].HeadingDegrees!.Value, 1e-9);
        Assert.Equal(input[1].OffsetSeconds, result[1].OffsetSeconds);
        Assert.Equal(input[1].SourceName, result[1].SourceName);
    }

    [Theory]
    [InlineData(DataQuality.Measured)]
    [InlineData(DataQuality.Published)]
    [InlineData(DataQuality.Reconstructed)]
    public void FillMissing_DerivedAngles_DowngradeQualityToEstimated(DataQuality original)
    {
        TrajectoryPoint[] input = [At(0, 0, 0, quality: original), At(1, 0, 1, quality: original)];

        var result = OrientationCalculator.FillMissing(input);

        Assert.All(result, p => Assert.Equal(DataQuality.Estimated, p.Quality));
        Assert.All(input, p => Assert.Equal(original, p.Quality));
    }

    [Fact]
    public void FillMissing_DerivedAngles_NeverUpgradeSimulatedOrUnknown()
    {
        TrajectoryPoint[] input =
        [
            At(0, 0, 0, quality: DataQuality.Simulated),
            At(1, 0, 1, quality: DataQuality.Unknown),
        ];

        var result = OrientationCalculator.FillMissing(input);

        Assert.Equal(DataQuality.Simulated, result[0].Quality);
        Assert.Equal(DataQuality.Unknown, result[1].Quality);
    }

    [Fact]
    public void FillMissing_CompleteOrientation_KeepsQuality()
    {
        TrajectoryPoint[] input =
        [
            At(0, 0, 0, quality: DataQuality.Measured, heading: 90, pitch: 0, roll: 0),
            At(1, 0, 1, quality: DataQuality.Measured, heading: 90, pitch: 0, roll: 0),
        ];

        var result = OrientationCalculator.FillMissing(input);

        Assert.Same(input[0], result[0]);
        Assert.Same(input[1], result[1]);
        Assert.All(result, p => Assert.Equal(DataQuality.Measured, p.Quality));
    }

    [Fact]
    public void FillMissing_VerticalSegmentMidPath_ReusesPreviousHeadingAndPitchIs90()
    {
        TrajectoryPoint[] input =
        [
            At(0, 0, 0, 0),
            At(10, 0, 1, 0),
            At(20, 0, 1, 1000),
            At(30, 0, 2, 1000),
        ];

        var result = OrientationCalculator.FillMissing(input);

        Assert.Equal(90, result[1].HeadingDegrees!.Value, 1e-9);
        Assert.Equal(90, result[1].PitchDegrees!.Value, 1e-9);
        Assert.Equal(0, result[0].PitchDegrees!.Value, 1e-9);
        Assert.Equal(90, result[2].HeadingDegrees!.Value, 1e-9);
        Assert.Equal(0, result[2].PitchDegrees!.Value, 1e-9);
        Assert.Equal(90, result[3].HeadingDegrees!.Value, 1e-9);
    }

    [Fact]
    public void FillMissing_NoMotionAtAll_LeftUnknown()
    {
        TrajectoryPoint[] input = [At(0, 5, 5, 10), At(1, 5, 5, 10)];

        var result = OrientationCalculator.FillMissing(input);

        Assert.All(result, p => Assert.False(p.HasOrientation));
    }

    [Fact]
    public void FillMissing_SinglePoint_Unchanged()
    {
        TrajectoryPoint[] input = [At(0, 5, 5)];

        Assert.Same(input[0], Assert.Single(OrientationCalculator.FillMissing(input)));
    }

    [Fact]
    public void FillMissing_UnsortedOffsets_Throws()
    {
        Assert.Throws<ArgumentException>(() => OrientationCalculator.FillMissing([At(1, 0, 0), At(0, 0, 1)]));
    }
}
