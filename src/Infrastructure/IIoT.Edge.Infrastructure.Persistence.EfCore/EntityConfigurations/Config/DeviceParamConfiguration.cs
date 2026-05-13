using IIoT.Edge.Domain.Config.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.EntityConfigurations.Config;

public class DeviceParamConfiguration
    : IEntityTypeConfiguration<DeviceParamEntity>
{
    public void Configure(
        EntityTypeBuilder<DeviceParamEntity> builder)
    {
        builder.ToTable("cfg_device_param");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.NetworkDeviceId)
            .IsRequired()
            .HasColumnName("network_device_id");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("name");

        builder.Property(x => x.Value)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("value");

        builder.Property(x => x.Unit)
            .HasMaxLength(20)
            .HasColumnName("unit");

        builder.Property(x => x.MinValue)
            .HasMaxLength(50)
            .HasColumnName("min_value");

        builder.Property(x => x.MaxValue)
            .HasMaxLength(50)
            .HasColumnName("max_value");

        builder.Property(x => x.SortOrder)
            .HasColumnName("sort_order");

        builder.HasOne(x => x.NetworkDevice)
            .WithMany()
            .HasForeignKey(x => x.NetworkDeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.NetworkDeviceId)
            .HasDatabaseName(
                "ix_cfg_device_param_device_id");
    }
}