using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Simulation;
using StarshipMissionTracker.Telemetry.Tests.Support;

namespace StarshipMissionTracker.Telemetry.Tests;

public class PathReplaySamplerTests
{
    private static PathReplaySampler Create(double interval = 1) =>
        new(TestData.Mission(), new LaunchClock(TestData.Start), interval);

    private static string Key(TelemetrySample s) => $"{s.Vehicle}@{(s.TimestampUtc - TestData.Start).TotalSeconds:0.###}";

    [Fact]
    public void NothingBeforeTheFirstOffset()
    {
        Assert.Empty(Create().Advance(-5, TestData.Start));
    }

    [Fact]
    public void SamplesAreSimulated_AndExactAtSourcePoints()
    {
        var samples = Create().Advance(10, TestData.Start);
        Assert.All(samples, s =>
        {
            Assert.Equal(DataQuality.Simulated, s.Quality);
            Assert.True(s.IsEstimated);
            Assert.Equal("Internal simulator", s.SourceName);
        });

        var ship10 = samples.Single(s => s.Vehicle == VehicleType.Starship && s.TimestampUtc == TestData.Start.AddSeconds(10));
        Assert.Equal(25.0, ship10.Latitude);
        Assert.Equal(-96.9, ship10.Longitude);
        Assert.Equal(5000, ship10.AltitudeMeters);
        Assert.Equal(700, ship10.VelocityMetersPerSecond);
        Assert.Equal(TestData.Start, ship10.ReceivedAtUtc);
    }

    [Fact]
    public void VelocityIsFiniteDifferenceWhenMissing_NeverInvented()
    {
        var samples = Create().Advance(20, TestData.Start);
        var first = samples.First(s => s.Vehicle == VehicleType.Starship);
        Assert.NotNull(first.VelocityMetersPerSecond);
        Assert.True(first.VelocityMetersPerSecond > 0);
        Assert.NotNull(first.HeadingDegrees);
    }

    [Fact]
    public void EachVehicleEndsWithOneFinalSampleAtItsLastPoint()
    {
        var sampler = Create();
        var samples = sampler.Advance(1000, TestData.Start);
        Assert.Equal(9, samples.Count(s => s.Vehicle == VehicleType.SuperHeavy));
        Assert.Equal(21, samples.Count(s => s.Vehicle == VehicleType.Starship));
        Assert.Equal(TestData.Start.AddSeconds(8), samples.Where(s => s.Vehicle == VehicleType.SuperHeavy).Max(s => s.TimestampUtc));
        Assert.True(sampler.IsComplete);
        Assert.Empty(sampler.Advance(2000, TestData.Start));
    }

    [Fact]
    public void DefaultSampleInterval_IsHalfSecond_MatchingTheLiveRefresh()
    {
        Assert.Equal(0.5, new SimulationOptions().SampleIntervalSeconds);
    }

    [Fact]
    public void HalfSecondInterval_GivesTwiceTheSamples_AllStillSimulated()
    {
        var samples = Create(interval: 0.5).Advance(1000, TestData.Start);
        var ship = samples.Where(s => s.Vehicle == VehicleType.Starship).ToList();

        Assert.Equal(41, ship.Count); // 0..20 s every 0.5 s
        Assert.Equal(0.5, (ship[1].TimestampUtc - ship[0].TimestampUtc).TotalSeconds);
        Assert.All(samples, s =>
        {
            Assert.Equal(DataQuality.Simulated, s.Quality);
            Assert.True(s.IsEstimated);
        });
    }

    [Fact]
    public void FinalSampleIsAddedWhenLastPointIsOffTheGrid()
    {
        var sampler = Create(interval: 3);
        var ship = sampler.Advance(1000, TestData.Start).Where(s => s.Vehicle == VehicleType.Starship).ToList();
        Assert.Equal([0, 3, 6, 9, 12, 15, 18, 20], ship.Select(s => (s.TimestampUtc - TestData.Start).TotalSeconds));
    }

    [Fact]
    public void FractionalInterval_LastPointCoincidingWithGridYieldsOneFinalSample()
    {
        var last = Math.BitIncrement(3 * 0.1);
        var path = new FlightPath(TrajectoryType.Planned, DataQuality.Estimated, "frac", null,
        [
            new TrajectoryPoint(0, 25.0, -97.0, 0, null, DataQuality.Estimated, "test"),
            new TrajectoryPoint(last, 25.0, -96.9, 100, null, DataQuality.Estimated, "test"),
        ]);
        var mission = new Mission(
            new MissionConfig("frac", "Frac", TestData.Start, null, []),
            new LaunchClock(TestData.Start),
            [new Vehicle("Ship", VehicleType.Starship, null, [path])]);
        var sampler = new PathReplaySampler(mission, new LaunchClock(TestData.Start), 0.1);

        var samples = sampler.Advance(10, TestData.Start);

        Assert.Equal(4, samples.Count);
        Assert.Equal(samples.Count, samples.Select(s => s.TimestampUtc).Distinct().Count());
        Assert.True(sampler.IsComplete);
        Assert.Empty(sampler.Advance(20, TestData.Start));
    }

    [Fact]
    public void SampleSet_IsIndependentOfTickPattern()
    {
        var reference = Create().Advance(1000, TestData.Start).Select(Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

        var jittery = Create();
        var collected = new List<string>();
        double t = -3;
        var steps = new[] { 0.3, 7.9, 0.01, 2.5, 13, 0.7, 40 };
        for (var i = 0; t < 30; i++)
        {
            t += steps[i % steps.Length];
            collected.AddRange(jittery.Advance(t, TestData.Start).Select(Key));
        }

        Assert.Equal(reference, collected.OrderBy(k => k, StringComparer.Ordinal).ToList());
        Assert.Equal(collected.Count, collected.Distinct().Count());
    }

    [Fact]
    public void WithinOneAdvance_SamplesAreInOrderPerVehicle()
    {
        var samples = Create().Advance(1000, TestData.Start);
        foreach (var group in samples.GroupBy(s => s.Vehicle))
        {
            var times = group.Select(s => s.TimestampUtc).ToList();
            Assert.Equal(times.OrderBy(x => x).ToList(), times);
        }
    }

    [Fact]
    public void RestoreRollsBackEmission()
    {
        var sampler = Create();
        sampler.Advance(5, TestData.Start);
        var snapshot = sampler.Snapshot();
        var lost = sampler.Advance(12, TestData.Start);
        sampler.Restore(snapshot);
        var again = sampler.Advance(12, TestData.Start);
        Assert.Equal(lost.Select(Key), again.Select(Key));
    }

    [Fact]
    public void FirstGridIndex_ToleratesFractionalIntervalRounding()
    {
        var path = new FlightPath(TrajectoryType.Planned, DataQuality.Estimated, "frac", null,
            [new TrajectoryPoint(1.1, 25.0, -97.0, 0, null, DataQuality.Estimated, "t"),
             new TrajectoryPoint(2.0, 25.0, -96.9, 100, null, DataQuality.Estimated, "t")]);
        var mission = new Mission(
            new MissionConfig("frac", "Frac", TestData.Start, null, []),
            new LaunchClock(TestData.Start),
            [new Vehicle("Ship", VehicleType.Starship, null, [path])]);
        var sampler = new PathReplaySampler(mission, new LaunchClock(TestData.Start), 0.1);

        var samples = sampler.Advance(2.0, TestData.Start);

        Assert.Contains(samples, s => Math.Abs((s.TimestampUtc - TestData.Start).TotalSeconds - 1.1) < 1e-6);
    }
}
