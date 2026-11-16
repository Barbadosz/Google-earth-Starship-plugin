using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace StarshipMissionTracker.Data.Tests;

public class MigrationTests
{
    private sealed class FileFactory(string path) : IDbContextFactory<StarshipDbContext>
    {
        public StarshipDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<StarshipDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"starship-test-{Guid.NewGuid():N}.db");

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Initialize_EmptyFileDatabase_AppliesInitialCreateAndEnablesWal()
    {
        var path = TempDbPath();
        try
        {
            var factory = new FileFactory(path);
            await new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(Ct);

            await using var db = factory.CreateDbContext();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(Ct), m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
            Assert.Equal(0, await db.Missions.CountAsync(Ct));

            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", Convert.ToString(await command.ExecuteScalarAsync(Ct)));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task Initialize_Twice_IsHarmless()
    {
        var path = TempDbPath();
        try
        {
            var initializer = new DatabaseInitializer(new FileFactory(path), NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync(Ct);
            await initializer.InitializeAsync(Ct);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task Model_HasNoPendingChangesComparedToMigrations()
    {
        using var test = await TestDatabase.CreateAsync();
        using var db = test.NewContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migration_CreatesAllSevenTables()
    {
        using var test = await TestDatabase.CreateAsync();
        using var db = test.NewContext();
        var connection = db.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE '\\_\\_%' ESCAPE '\\' AND name NOT LIKE 'sqlite%' ORDER BY name;";
        var names = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }

        string[] expected = ["DataSources", "MissionEvents", "Missions", "TelemetrySamples", "TrajectoryPoints", "Trajectories", "Vehicles"];
        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), names.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void DesignTimeFactory_CreatesContextWithoutOpeningTheDatabase()
    {
        using var db = new StarshipDesignTimeFactory().CreateDbContext([]);

        Assert.True(db.Database.IsSqlite());
    }

    [Fact]
    public async Task DeletingMission_CascadesToVehiclesPointsEventsAndSamples()
    {
        using var test = await TestDatabase.CreateAsync();
        await test.Missions.ImportAsync(TestData.Mission(), true, Ct);
        await test.Telemetry.AddRangeAsync(TestData.Code, [TestData.Sample(StarshipMissionTracker.Core.Enums.VehicleType.Starship, TestData.T0.AddSeconds(1))], Ct);

        await using var db = test.NewContext();
        await db.Missions.ExecuteDeleteAsync(Ct);

        Assert.Equal(0, await db.Vehicles.CountAsync(Ct));
        Assert.Equal(0, await db.Trajectories.CountAsync(Ct));
        Assert.Equal(0, await db.TrajectoryPoints.CountAsync(Ct));
        Assert.Equal(0, await db.MissionEvents.CountAsync(Ct));
        Assert.Equal(0, await db.TelemetrySamples.CountAsync(Ct));
        Assert.Equal(2, await db.DataSources.CountAsync(Ct));
    }
}
