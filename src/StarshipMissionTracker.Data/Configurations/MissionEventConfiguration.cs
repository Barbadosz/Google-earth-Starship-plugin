using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Configurations;

internal sealed class MissionEventConfiguration : IEntityTypeConfiguration<MissionEventEntity>
{
    public void Configure(EntityTypeBuilder<MissionEventEntity> builder)
    {
        builder.ToTable("MissionEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired();
        builder.Property(e => e.SourceName).IsRequired();
        builder.HasIndex(e => new { e.MissionId, e.OffsetSeconds });
        builder.HasOne<VehicleEntity>().WithMany().HasForeignKey(e => e.VehicleId).OnDelete(DeleteBehavior.SetNull);
    }
}
