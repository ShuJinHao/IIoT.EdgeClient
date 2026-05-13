using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.Domain.Hardware.Aggregates;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore;

public class EdgeDbContext(
    DbContextOptions<EdgeDbContext> options)
    : DbContext(options)
{
    // Hardware
    public DbSet<NetworkDeviceEntity> NetworkDevices
        => Set<NetworkDeviceEntity>();
    public DbSet<SerialDeviceEntity> SerialDevices
        => Set<SerialDeviceEntity>();
    public DbSet<IoMappingEntity> IoMappings
        => Set<IoMappingEntity>();
    public DbSet<PlcTaskBindingEntity> PlcTaskBindings
        => Set<PlcTaskBindingEntity>();

    // Config
    public DbSet<SystemConfigEntity> SystemConfigs
        => Set<SystemConfigEntity>();
    public DbSet<DeviceParamEntity> DeviceParams
        => Set<DeviceParamEntity>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
