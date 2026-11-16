using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Configurations;

internal sealed class MissionConfiguration : IEntityTypeConfiguration<MissionEntity>
{
    public void Configure(EntityTypeBuilder<MissionEntity> builder)
    {
        builder.ToTable("Missions");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.MissionCode).IsRequired();
        builder.Property(m => m.Name).IsRequired();
        builder.HasIndex(m => m.MissionCode).IsUnique();
        builder.HasMany(m => m.Vehicles).WithOne(v => v.Mission).HasForeignKey(v => v.MissionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(m => m.Events).WithOne(e => e.Mission).HasForeignKey(e => e.MissionId).OnDelete(DeleteBehavior.Cascade);
    }
}
