using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarshipMissionTracker.Data.Entities;

namespace StarshipMissionTracker.Data.Configurations;

internal sealed class DataSourceConfiguration : IEntityTypeConfiguration<DataSourceEntity>
{
    public void Configure(EntityTypeBuilder<DataSourceEntity> builder)
    {
        builder.ToTable("DataSources");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique();
    }
}
