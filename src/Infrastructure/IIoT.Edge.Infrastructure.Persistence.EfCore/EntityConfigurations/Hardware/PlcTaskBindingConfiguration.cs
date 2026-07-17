using IIoT.Edge.Domain.Hardware.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.EntityConfigurations.Hardware;

public sealed class PlcTaskBindingConfiguration : IEntityTypeConfiguration<PlcTaskBindingEntity>
{
    public void Configure(EntityTypeBuilder<PlcTaskBindingEntity> builder)
    {
        builder.ToTable("hw_plc_task_binding");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.NetworkDeviceId)
            .HasColumnName("network_device_id")
            .IsRequired();

        builder.Property(x => x.TaskKey)
            .HasColumnName("task_key")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Enabled)
            .HasColumnName("enabled")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(x => new { x.NetworkDeviceId, x.TaskKey })
            .IsUnique()
            .HasDatabaseName("ux_hw_plc_task_binding_device_task");

    }
}
