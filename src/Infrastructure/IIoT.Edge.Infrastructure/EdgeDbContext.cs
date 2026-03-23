using IIoT.Edge.Domain.Hardware.Aggregates;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace IIoT.Edge.Infrastructure;

public class EdgeDbContext(DbContextOptions<EdgeDbContext> options) : DbContext(options)
{
    public DbSet<NetworkDeviceEntity> NetworkDevices => Set<NetworkDeviceEntity>();
    public DbSet<SerialDeviceEntity> SerialDevices => Set<SerialDeviceEntity>();
    public DbSet<IoMappingEntity> IoMappings => Set<IoMappingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}