using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<VehicleEntity>
{
    public void Configure(EntityTypeBuilder<VehicleEntity> builder)
    {
        builder.ToTable("Vehicles");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).IsRequired();
        builder.HasIndex(v => new { v.MissionId, v.Type }).IsUnique();
    }
}
