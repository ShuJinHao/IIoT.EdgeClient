using IIoT.Edge.Domain.Config.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.EntityConfigurations.Config;

public class SystemConfigConfiguration
    : IEntityTypeConfiguration<SystemConfigEntity>
{
    public void Configure(
        EntityTypeBuilder<SystemConfigEntity> builder)
    {
        builder.ToTable("cfg_system_config");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Key)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("key");

        builder.Property(x => x.Value)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("value");

        builder.Property(x => x.Description)
            .HasMaxLength(200)
            .HasColumnName("description");

        builder.Property(x => x.SortOrder)
            .HasColumnName("sort_order");

        builder.HasIndex(x => x.Key)
            .IsUnique()
            .HasDatabaseName("ix_cfg_system_config_key");
    }
}