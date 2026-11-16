using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace StarshipMissionTracker.Data;

/// <summary>Applies the migrations and switches the SQLite file to WAL mode (the simulator writes while the API reads).</summary>
public sealed class DatabaseInitializer(IDbContextFactory<StarshipDbContext> contextFactory, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        EnsureDirectory(db.Database.GetConnectionString());

        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            // journal_mode is persisted in the file; it cannot change inside a transaction, so it runs before the migrations.
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            var mode = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            logger.LogInformation("SQLite journal mode: {JournalMode}", mode);
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database is up to date");
    }

    // synchronous=NORMAL is deliberately not set here: it is a per-connection setting (not stored in the file), so running it on one
    // initializer connection would not affect the pooled connections the application uses later.

    /// <summary>Creates the database file's directory so a missing folder does not surface as a vague "unable to open database file".</summary>
    private void EnsureDirectory(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var file = builder.DataSource;
        if (string.IsNullOrWhiteSpace(file) || file == ":memory:" || builder.Mode == SqliteOpenMode.Memory || file.StartsWith("file:", StringComparison.Ordinal))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            logger.LogInformation("Created database directory {Directory}", directory);
        }
    }
}
