using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StarshipMissionTracker.Data;

/// <summary>Lets <c>dotnet ef migrations</c> work without starting the web host. Never opens the database.</summary>
public sealed class StarshipDesignTimeFactory : IDesignTimeDbContextFactory<StarshipDbContext>
{
    public StarshipDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StarshipDbContext>()
            .UseSqlite("Data Source=starship.design-time.db")
            .Options;
        return new StarshipDbContext(options);
    }
}
