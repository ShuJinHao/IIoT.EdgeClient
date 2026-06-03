using IIoT.Edge.Domain.Hardware.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.EntityConfigurations.Hardware;

public class IoMappingConfiguration : IEntityTypeConfiguration<IoMappingEntity>
{
    public void Configure(EntityTypeBuilder<IoMappingEntity> builder)
    {
        builder.ToTable("hw_io_mapping");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(x => x.NetworkDeviceId).HasColumnName("network_device_id");

        builder.Property(x => x.SignalKey)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("signal_key");

        builder.Property(x => x.PlcAddress)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("plc_address");

        builder.Property(x => x.AddressCount).HasColumnName("address_count");

        builder.Property(x => x.DataType)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnName("data_type");

        builder.Property(x => x.Direction)
            .IsRequired()
            .HasMaxLength(10)
            .HasColumnName("direction");

        builder.Property(x => x.Category)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("category");

        builder.Property(x => x.BusinessGroup)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("business_group");

        builder.Property(x => x.SortOrder).HasColumnName("sort_order");
        builder.Property(x => x.Remark).HasMaxLength(500).HasColumnName("remark");

        builder.HasOne(x => x.NetworkDevice)
            .WithMany(x => x.IoMappings)
            .HasForeignKey(x => x.NetworkDeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.NetworkDeviceId)
            .HasDatabaseName("ix_hw_io_mapping_network_device_id");
    }
}
