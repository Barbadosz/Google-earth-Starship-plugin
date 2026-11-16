using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Configurations;

internal sealed class TelemetrySampleConfiguration : IEntityTypeConfiguration<TelemetrySampleEntity>
{
    public void Configure(EntityTypeBuilder<TelemetrySampleEntity> builder)
    {
        builder.ToTable("TelemetrySamples");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();
        builder.HasIndex(s => new { s.VehicleId, s.TimestampUtc }).IsUnique();
        builder.HasOne(s => s.Vehicle).WithMany().HasForeignKey(s => s.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Source).WithMany().HasForeignKey(s => s.SourceId).OnDelete(DeleteBehavior.Restrict);
    }
}
