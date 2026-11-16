using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Configurations;

internal sealed class TrajectoryConfiguration : IEntityTypeConfiguration<TrajectoryEntity>
{
    public void Configure(EntityTypeBuilder<TrajectoryEntity> builder)
    {
        builder.ToTable("Trajectories");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.SourceName).IsRequired();
        builder.HasIndex(t => new { t.VehicleId, t.Type }).IsUnique();
        builder.HasOne(t => t.Mission).WithMany().HasForeignKey(t => t.MissionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Vehicle).WithMany(v => v.Trajectories).HasForeignKey(t => t.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Source).WithMany().HasForeignKey(t => t.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(t => t.Points).WithOne(p => p.Trajectory).HasForeignKey(p => p.TrajectoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
