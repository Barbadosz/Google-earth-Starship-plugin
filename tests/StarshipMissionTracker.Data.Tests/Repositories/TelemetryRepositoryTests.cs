using Microsoft.EntityFrameworkCore;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Data.Tests.Repositories;

public class TelemetryRepositoryTests
{
    private static readonly DateTimeOffset Start = TestData.T0;

    private static async Task<TestDatabase> NewImportedAsync()
    {
        var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), true, Ct);
        return db;
    }

    [Fact]
    public async Task AddRange_ThenGetSamples_RoundTripsAllFieldsAndUtcPrecision()
    {
        using var db = await NewImportedAsync();
        var ts = Start.AddTicks(1234567).AddSeconds(3);
        var sample = new TelemetrySample(VehicleType.Starship, ts, ts.AddMilliseconds(7), 25.5, -97.25, 1234.5, 321.5, 123.5, 12.5, -3.5,
            DataQuality.Simulated, DataSourceNames.InternalSimulator);

        await db.Telemetry.AddRangeAsync(TestData.Code, [sample], Ct);
        var back = Assert.Single(await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 10, Ct));

        Assert.Equal(sample, back);
        Assert.Equal(ts.UtcTicks, back.TimestampUtc.UtcTicks);
        Assert.Equal(TimeSpan.Zero, back.TimestampUtc.Offset);
        Assert.Equal(DataQuality.Simulated, back.Quality);
        Assert.Equal(DataSourceNames.InternalSimulator, back.SourceName);
    }

    [Fact]
    public async Task AddRange_SampleWithoutPosition_IsStoredWithNulls()
    {
        using var db = await NewImportedAsync();
        var s = TestData.Sample(VehicleType.Starship, Start.AddSeconds(1), lat: null, lon: null, alt: null);

        await db.Telemetry.AddRangeAsync(TestData.Code, [s], Ct);

        var back = Assert.Single(await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 10, Ct));
        Assert.False(back.HasPosition);
        Assert.Null(back.AltitudeMeters);
    }

    [Fact]
    public async Task AddRange_UnknownMission_Throws()
    {
        using var db = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Telemetry.AddRangeAsync("nope", [TestData.Sample(VehicleType.Starship, Start)], Ct));
    }

    [Fact]
    public async Task AddRange_UnregisteredSourceWithNonSimulatedQuality_Throws()
    {
        using var db = await NewImportedAsync();
        var s = TestData.Sample(VehicleType.Starship, Start, quality: DataQuality.Measured, source: "mystery feed");

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Telemetry.AddRangeAsync(TestData.Code, [s], Ct));
        Assert.Empty(await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 10, Ct));
    }

    [Fact]
    public async Task AddRange_FailureInBatch_StoresNothing()
    {
        using var db = await NewImportedAsync();
        var ok = TestData.Sample(VehicleType.Starship, Start.AddSeconds(1));
        var duplicate = TestData.Sample(VehicleType.Starship, Start.AddSeconds(1));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Telemetry.AddRangeAsync(TestData.Code, [ok, duplicate], Ct));

        Assert.Empty(await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 10, Ct));
    }

    [Fact]
    public async Task GetLatest_ReturnsNewestPerVehicleAndOmitsVehiclesWithoutSamples()
    {
        using var db = await NewImportedAsync();
        await db.Telemetry.AddRangeAsync(TestData.Code,
        [
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(1), alt: 100),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(3), alt: 300),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(2), alt: 200),
        ], Ct);

        var latest = await db.Telemetry.GetLatestAsync(TestData.Code, Ct);

        var one = Assert.Single(latest);
        Assert.Equal(VehicleType.Starship, one.Vehicle);
        Assert.Equal(300, one.AltitudeMeters);

        await db.Telemetry.AddRangeAsync(TestData.Code, [TestData.Sample(VehicleType.SuperHeavy, Start.AddSeconds(9), alt: 9)], Ct);
        Assert.Equal(2, (await db.Telemetry.GetLatestAsync(TestData.Code, Ct)).Count);
    }

    [Fact]
    public async Task GetHistory_ReturnsActualPathWithWorstQualityAndOffsetsFromT0()
    {
        using var db = await NewImportedAsync();
        await db.Telemetry.AddRangeAsync(TestData.Code,
        [
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(1), quality: DataQuality.Simulated),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(2.5), quality: DataQuality.Simulated),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(4), lat: null, lon: null, alt: null),
        ], Ct);

        var path = await db.Telemetry.GetHistoryAsync(TestData.Code, VehicleType.Starship, 100, Ct);

        Assert.NotNull(path);
        Assert.Equal(TrajectoryType.Actual, path.Type);
        Assert.Equal(DataQuality.Simulated, path.Quality);
        Assert.Equal(DataSourceNames.InternalSimulator, path.SourceName);
        Assert.Equal([1d, 2.5], path.Points.Select(p => p.OffsetSeconds).ToArray());
        Assert.All(path.Points, p => Assert.Equal(DataQuality.Simulated, p.Quality));
    }

    [Fact]
    public async Task GetHistory_MixedQualities_PathTakesTheWorst()
    {
        using var db = await NewImportedAsync();
        await db.Telemetry.AddRangeAsync(TestData.Code,
        [
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(1), quality: DataQuality.Simulated),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(2), quality: DataQuality.Unknown, source: DataSourceNames.PlannedTrajectoryFile),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(3), quality: DataQuality.Simulated),
        ], Ct);

        var path = await db.Telemetry.GetHistoryAsync(TestData.Code, VehicleType.Starship, 100, Ct);

        Assert.Equal(DataQuality.Unknown, path!.Quality);
        Assert.Equal($"{DataSourceNames.InternalSimulator}, {DataSourceNames.PlannedTrajectoryFile}", path.SourceName);
    }

    [Fact]
    public async Task AddRange_SimulatorSourceWithNonSimulatedQuality_ThrowsAndStoresNothing()
    {
        using var db = await NewImportedAsync();
        var ok = TestData.Sample(VehicleType.Starship, Start.AddSeconds(1));
        var bad = TestData.Sample(VehicleType.Starship, Start.AddSeconds(2), quality: DataQuality.Estimated);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Telemetry.AddRangeAsync(TestData.Code, [ok, bad], Ct));

        Assert.Empty(await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 10, Ct));
    }

    [Fact]
    public async Task AddRange_QualityBetterThanSourceReliability_ThrowsAndStoresNothing()
    {
        using var db = await NewImportedAsync();
        var bad = TestData.Sample(VehicleType.Starship, Start.AddSeconds(1), quality: DataQuality.Simulated, source: DataSourceNames.PlannedTrajectoryFile);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Telemetry.AddRangeAsync(TestData.Code, [bad], Ct));

        Assert.Empty(await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 10, Ct));
    }

    [Fact]
    public async Task AddRange_UnknownSourceNameEvenWithSimulatedQuality_IsNotAutoCreated()
    {
        using var db = await NewImportedAsync();
        var bad = TestData.Sample(VehicleType.Starship, Start.AddSeconds(1), quality: DataQuality.Simulated, source: "NASA feed");

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Telemetry.AddRangeAsync(TestData.Code, [bad], Ct));

        await using var ctx = db.NewContext();
        Assert.False(await ctx.DataSources.AnyAsync(s => s.Name == "NASA feed", Ct));
    }

    [Fact]
    public async Task GetHistory_TooManyRows_DecimatesKeepingFirstAndLast()
    {
        using var db = await NewImportedAsync();
        var samples = Enumerable.Range(0, 101).Select(i => TestData.Sample(VehicleType.Starship, Start.AddSeconds(i), alt: i)).ToList();
        await db.Telemetry.AddRangeAsync(TestData.Code, samples, Ct);

        var path = await db.Telemetry.GetHistoryAsync(TestData.Code, VehicleType.Starship, 11, Ct);

        Assert.NotNull(path);
        Assert.Equal(11, path.Points.Count);
        Assert.Equal(0, path.Points[0].OffsetSeconds);
        Assert.Equal(100, path.Points[^1].OffsetSeconds);
        Assert.All(path.Points, p => Assert.Equal(p.OffsetSeconds, p.AltitudeMeters));
    }

    [Fact]
    public async Task GetHistory_NoPositionsUnknownVehicleOrMission_ReturnsNull()
    {
        using var db = await NewImportedAsync();
        await db.Telemetry.AddRangeAsync(TestData.Code, [TestData.Sample(VehicleType.Starship, Start, lat: null, lon: null)], Ct);

        Assert.Null(await db.Telemetry.GetHistoryAsync(TestData.Code, VehicleType.Starship, 10, Ct));
        Assert.Null(await db.Telemetry.GetHistoryAsync(TestData.Code, VehicleType.SuperHeavy, 10, Ct));
        Assert.Null(await db.Telemetry.GetHistoryAsync("nope", VehicleType.Starship, 10, Ct));
    }

    [Fact]
    public async Task GetSamples_FiltersByVehicleSinceAndLimit()
    {
        using var db = await NewImportedAsync();
        await db.Telemetry.AddRangeAsync(TestData.Code,
        [
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(1)),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(2)),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(3)),
            TestData.Sample(VehicleType.SuperHeavy, Start.AddSeconds(2)),
        ], Ct);

        var starship = await db.Telemetry.GetSamplesAsync(TestData.Code, VehicleType.Starship, null, 100, Ct);
        var since = await db.Telemetry.GetSamplesAsync(TestData.Code, VehicleType.Starship, Start.AddSeconds(2), 100, Ct);
        var limited = await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 2, Ct);

        Assert.Equal(3, starship.Count);
        Assert.Equal([Start.AddSeconds(2), Start.AddSeconds(3)], since.Select(s => s.TimestampUtc).ToArray());
        Assert.Equal(2, limited.Count);
        Assert.Empty(await db.Telemetry.GetSamplesAsync("nope", null, null, 10, Ct));
    }

    [Fact]
    public async Task DeleteSimulated_RemovesOnlySimulatedSamplesOfThatMission()
    {
        using var db = await NewImportedAsync();
        await db.Missions.ImportAsync(TestData.Mission("other", new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)), true, Ct);
        await db.Telemetry.AddRangeAsync(TestData.Code,
        [
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(1), quality: DataQuality.Simulated),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(2), quality: DataQuality.Simulated),
            TestData.Sample(VehicleType.Starship, Start.AddSeconds(3), quality: DataQuality.Unknown, source: DataSourceNames.PlannedTrajectoryFile),
        ], Ct);
        await db.Telemetry.AddRangeAsync("other", [TestData.Sample(VehicleType.Starship, Start.AddSeconds(1))], Ct);

        var deleted = await db.Telemetry.DeleteSimulatedAsync(TestData.Code, Ct);

        Assert.Equal(2, deleted);
        var left = Assert.Single(await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 10, Ct));
        Assert.Equal(DataQuality.Unknown, left.Quality);
        Assert.Single(await db.Telemetry.GetSamplesAsync("other", null, null, 10, Ct));
        Assert.Equal(0, await db.Telemetry.DeleteSimulatedAsync("nope", Ct));
    }
}
