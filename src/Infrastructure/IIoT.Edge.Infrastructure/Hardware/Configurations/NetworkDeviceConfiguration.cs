using IIoT.Edge.Domain.Hardware.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.Edge.Infrastructure.Hardware.Configurations;

public class NetworkDeviceConfiguration : IEntityTypeConfiguration<NetworkDeviceEntity>
{
    public void Configure(EntityTypeBuilder<NetworkDeviceEntity> builder)
    {
        builder.ToTable("hw_network_device");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(x => x.DeviceName)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("device_name");

        builder.Property(x => x.DeviceType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasColumnName("device_type");

        builder.Property(x => x.DeviceModel)
            .HasMaxLength(20)
            .HasColumnName("device_model");

        builder.Property(x => x.IpAddress)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("ip_address");

        builder.Property(x => x.Port1).HasColumnName("port1");
        builder.Property(x => x.Port2).HasColumnName("port2");
        builder.Property(x => x.SendCmd1).HasMaxLength(200).HasColumnName("send_cmd1");
        builder.Property(x => x.SendCmd2).HasMaxLength(200).HasColumnName("send_cmd2");
        builder.Property(x => x.ConnectTimeout).HasColumnName("connect_timeout");
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled");
        builder.Property(x => x.Remark).HasMaxLength(500).HasColumnName("remark");

        builder.HasMany(x => x.IoMappings)
            .WithOne(x => x.NetworkDevice)
            .HasForeignKey(x => x.NetworkDeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.IpAddress)
            .HasDatabaseName("ix_hw_network_device_ip");
    }
}