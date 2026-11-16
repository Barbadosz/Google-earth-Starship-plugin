using Microsoft.EntityFrameworkCore;
using StarshipMissionTracker.Core.Enums;
using StarshipMissionTracker.Data.Converters;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data;

public class StarshipDbContext(DbContextOptions<StarshipDbContext> options) : DbContext(options)
{
    public DbSet<MissionEntity> Missions => Set<MissionEntity>();
    public DbSet<VehicleEntity> Vehicles => Set<VehicleEntity>();
    public DbSet<DataSourceEntity> DataSources => Set<DataSourceEntity>();
    public DbSet<TrajectoryEntity> Trajectories => Set<TrajectoryEntity>();
    public DbSet<TrajectoryPointEntity> TrajectoryPoints => Set<TrajectoryPointEntity>();
    public DbSet<TelemetrySampleEntity> TelemetrySamples => Set<TelemetrySampleEntity>();
    public DbSet<MissionEventEntity> MissionEvents => Set<MissionEventEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Instants as UTC ticks; enums as readable strings (provenance must stay inspectable in the db).
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcInstantConverter>();
        configurationBuilder.Properties<MissionStatus>().HaveConversion<string>();
        configurationBuilder.Properties<VehicleType>().HaveConversion<string>();
        configurationBuilder.Properties<DataSourceType>().HaveConversion<string>();
        configurationBuilder.Properties<TrajectoryType>().HaveConversion<string>();
        configurationBuilder.Properties<DataQuality>().HaveConversion<string>();
        configurationBuilder.Properties<EventType>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StarshipDbContext).Assembly);
}
