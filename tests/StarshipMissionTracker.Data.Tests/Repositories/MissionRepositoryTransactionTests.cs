using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StarshipMissionTracker.Core.Enums;

namespace StarshipMissionTracker.Data.Tests.Repositories;

/// <summary>Transaction behaviour of the multi-step mission operations (SetPlannedLaunchTime, BeginSimulationRun).</summary>
public class MissionRepositoryTransactionTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 11, 15, 12, 5, 0, TimeSpan.Zero);

    /// <summary>
    /// The repository relies on EF's BeginTransactionAsync taking the write lock up front (Microsoft.Data.Sqlite begins
    /// non-deferred transactions with BEGIN IMMEDIATE), so a read-then-write cannot hit SQLITE_BUSY_SNAPSHOT under WAL.
    /// This guards against a provider change to deferred transactions.
    /// </summary>
    [Fact]
    public async Task EfTransaction_TakesTheWriteLockAtOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), $"starship-tx-{Guid.NewGuid():N}.db");
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            await using var db = new StarshipDbContext(new DbContextOptionsBuilder<StarshipDbContext>().UseSqlite(cs).Options);
            await db.Database.EnsureCreatedAsync(Ct);
            await using var tx = await db.Database.BeginTransactionAsync(Ct);

            await using var other = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 1 }.ToString());
            await other.OpenAsync(Ct);
            var cmd = other.CreateCommand();
            cmd.CommandText = "BEGIN IMMEDIATE";

            var ex = Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
            Assert.Equal(5, ex.SqliteErrorCode); // SQLITE_BUSY: the write lock is already held
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BeginSimulationRun_SetsAnchorStatusClearsActual_DeletesOnlyThisMissionsSimulatedSamples_KeepsPlanned()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), true, Ct);
        await db.Missions.ImportAsync(TestData.Mission("other"), true, Ct);
        await db.Missions.SetFlightStateAsync(TestData.Code, MissionStatus.Completed, Anchor, Anchor, cancellationToken: Ct);
        await db.Telemetry.AddRangeAsync(TestData.Code,
        [
            TestData.Sample(VehicleType.Starship, Anchor.AddSeconds(1)),
            TestData.Sample(VehicleType.Starship, Anchor.AddSeconds(2), quality: DataQuality.Unknown, source: DataSourceNames.PlannedTrajectoryFile),
        ], Ct);
        await db.Telemetry.AddRangeAsync("other", [TestData.Sample(VehicleType.Starship, Anchor.AddSeconds(1))], Ct);
        var newAnchor = Anchor.AddHours(1);
        db.Time.Now = db.Time.Now.AddMinutes(5);

        Assert.True(await db.Missions.BeginSimulationRunAsync(TestData.Code, newAnchor, Ct));

        var info = await db.Missions.GetInfoAsync(TestData.Code, Ct);
        Assert.Equal(MissionStatus.Scheduled, info!.Status);
        Assert.Equal(newAnchor, info.SimulationLaunchTimeUtc);
        Assert.Null(info.ActualLaunchTimeUtc);
        Assert.Equal(TestData.T0, info.PlannedLaunchTimeUtc);
        Assert.Equal(db.Time.Now, info.UpdatedAtUtc);
        var left = Assert.Single(await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 100, Ct));
        Assert.Equal(DataQuality.Unknown, left.Quality);
        Assert.Single(await db.Telemetry.GetSamplesAsync("other", null, null, 100, Ct));
    }

    [Fact]
    public async Task BeginSimulationRun_UnknownMission_ReturnsFalse_NonUtc_Throws()
    {
        using var db = await TestDatabase.CreateAsync();
        await db.Missions.ImportAsync(TestData.Mission(), true, Ct);

        Assert.False(await db.Missions.BeginSimulationRunAsync("nope", Anchor, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            db.Missions.BeginSimulationRunAsync(TestData.Code, new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(2)), Ct));
    }

    [Fact]
    public async Task SetPlannedLaunchTime_FailingDelete_RollsBackTheRowChanges()
    {
        using var db = await TestDatabase.CreateAsync();
        await SeedCompletedRunAsync(db);
        await BlockSampleDeletesAsync(db);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Missions.SetPlannedLaunchTimeAsync(TestData.Code, TestData.T0.AddDays(3), Ct));

        await AssertCompletedRunIntactAsync(db);
    }

    [Fact]
    public async Task BeginSimulationRun_FailingDelete_RollsBackTheRowChanges()
    {
        using var db = await TestDatabase.CreateAsync();
        await SeedCompletedRunAsync(db);
        await BlockSampleDeletesAsync(db);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Missions.BeginSimulationRunAsync(TestData.Code, Anchor.AddHours(2), Ct));

        await AssertCompletedRunIntactAsync(db);
    }

    private static async Task SeedCompletedRunAsync(TestDatabase db)
    {
        await db.Missions.ImportAsync(TestData.Mission(), true, Ct);
        await db.Missions.SetFlightStateAsync(TestData.Code, MissionStatus.Completed, Anchor, Anchor, cancellationToken: Ct);
        await db.Telemetry.AddRangeAsync(TestData.Code, [TestData.Sample(VehicleType.Starship, Anchor.AddSeconds(1))], Ct);
    }

    /// <summary>A trigger makes every sample delete fail, i.e. the step after the row update inside the transaction.</summary>
    private static async Task BlockSampleDeletesAsync(TestDatabase db)
    {
        await using var ctx = db.NewContext();
        await ctx.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER block_sample_delete BEFORE DELETE ON TelemetrySamples BEGIN SELECT RAISE(ABORT, 'delete blocked'); END;", Ct);
    }

    private static async Task AssertCompletedRunIntactAsync(TestDatabase db)
    {
        var info = await db.Missions.GetInfoAsync(TestData.Code, Ct);
        Assert.Equal(MissionStatus.Completed, info!.Status);
        Assert.Equal(TestData.T0, info.PlannedLaunchTimeUtc);
        Assert.Equal(Anchor, info.SimulationLaunchTimeUtc);
        Assert.Equal(Anchor, info.ActualLaunchTimeUtc);
        Assert.Single(await db.Telemetry.GetSamplesAsync(TestData.Code, null, null, 100, Ct));
    }
}
