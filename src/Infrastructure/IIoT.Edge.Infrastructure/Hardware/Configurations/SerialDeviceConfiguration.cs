using IIoT.Edge.Domain.Hardware.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.Edge.Infrastructure.Hardware.Configurations;

public class SerialDeviceConfiguration : IEntityTypeConfiguration<SerialDeviceEntity>
{
    public void Configure(EntityTypeBuilder<SerialDeviceEntity> builder)
    {
        builder.ToTable("hw_serial_device");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(x => x.DeviceName)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("device_name");

        builder.Property(x => x.DeviceType)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("device_type");

        builder.Property(x => x.PortName)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnName("port_name");

        builder.Property(x => x.BaudRate).HasColumnName("baud_rate");
        builder.Property(x => x.DataBits).HasColumnName("data_bits");

        builder.Property(x => x.StopBits)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnName("stop_bits");

        builder.Property(x => x.Parity)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnName("parity");

        builder.Property(x => x.SendCmd1).HasMaxLength(200).HasColumnName("send_cmd1");
        builder.Property(x => x.SendCmd2).HasMaxLength(200).HasColumnName("send_cmd2");
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled");
        builder.Property(x => x.Remark).HasMaxLength(500).HasColumnName("remark");

        builder.HasIndex(x => x.PortName)
            .HasDatabaseName("ix_hw_serial_device_port_name");
    }
}