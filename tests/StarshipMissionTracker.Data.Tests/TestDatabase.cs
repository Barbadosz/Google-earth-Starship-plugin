using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StarshipMissionTracker.Data.Repositories;

namespace StarshipMissionTracker.Data.Tests;

/// <summary>One open in-memory SQLite connection for the test lifetime, schema created by the real migrations.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<StarshipDbContext> _options;

    private TestDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<StarshipDbContext>().UseSqlite(_connection).Options;
        Factory = new Factories(_options);
        Time = new ManualTime(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        Missions = new MissionRepository(Factory, Time, NullLogger<MissionRepository>.Instance);
        Telemetry = new TelemetryRepository(Factory, NullLogger<TelemetryRepository>.Instance);
    }

    /// <summary>Creates the database and applies the real migrations, cancellable by the per-test token.</summary>
    public static async Task<TestDatabase> CreateAsync()
    {
        var db = new TestDatabase();
        try
        {
            await new DatabaseInitializer(db.Factory, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(Ct);
        }
        catch
        {
            db.Dispose();
            throw;
        }

        return db;
    }

    public IDbContextFactory<StarshipDbContext> Factory { get; }
    public ManualTime Time { get; }
    public MissionRepository Missions { get; }
    public TelemetryRepository Telemetry { get; }

    public StarshipDbContext NewContext() => Factory.CreateDbContext();

    public void Dispose() => _connection.Dispose();

    private sealed class Factories(DbContextOptions<StarshipDbContext> options) : IDbContextFactory<StarshipDbContext>
    {
        public StarshipDbContext CreateDbContext() => new(options);
    }

    public sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
