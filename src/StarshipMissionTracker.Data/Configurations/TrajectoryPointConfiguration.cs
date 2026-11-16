using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Configurations;

internal sealed class TrajectoryPointConfiguration : IEntityTypeConfiguration<TrajectoryPointEntity>
{
    public void Configure(EntityTypeBuilder<TrajectoryPointEntity> builder)
    {
        builder.ToTable("TrajectoryPoints");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();
        builder.Property(p => p.SourceName).IsRequired();
        builder.HasIndex(p => new { p.TrajectoryId, p.OffsetSeconds }).IsUnique();
    }
}
