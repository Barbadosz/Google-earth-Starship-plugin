using Microsoft.EntityFrameworkCore;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Repositories;

internal static class DataSourceStore
{
    /// <summary>Returns the tracked source with this name, adding it (not saved yet) when absent.</summary>
    public static async Task<DataSourceEntity> EnsureAsync(
        StarshipDbContext db, string name, DataSourceType type, DataQuality reliability, CancellationToken cancellationToken)
    {
        var existing = await db.DataSources.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new DataSourceEntity { Id = Guid.CreateVersion7(), Name = name, Type = type, Reliability = reliability };
        db.DataSources.Add(created);
        return created;
    }
}
