using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace StarshipMissionTracker.Data.Tests;

public class SmokeTests
{
    [Fact]
    public void InMemorySqlite_Opens_AndAnswersQuery()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void EfCoreSqlite_ProviderIsConfigurable()
    {
        var options = new DbContextOptionsBuilder().UseSqlite("DataSource=:memory:").Options;
        Assert.NotNull(options);
    }
}
