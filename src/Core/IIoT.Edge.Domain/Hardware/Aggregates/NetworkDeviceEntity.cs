using IIoT.Edge.Common.Domain;
using IIoT.Edge.Common.Enums;

namespace IIoT.Edge.Domain.Hardware.Aggregates;

public class NetworkDeviceEntity : BaseEntity<int>, IAggregateRoot
{
    protected NetworkDeviceEntity() { }

    public NetworkDeviceEntity(
        string deviceName,
        DeviceType deviceType,
        string ipAddress,
        int port1)
    {
        DeviceName = deviceName;
        DeviceType = deviceType;
        IpAddress = ipAddress;
        Port1 = port1;
    }

    public string DeviceName { get; set; } = null!;
    public DeviceType DeviceType { get; set; }
    public string? DeviceModel { get; set; }
    public string IpAddress { get; set; } = null!;
    public int Port1 { get; set; }
    public int? Port2 { get; set; }
    public string? SendCmd1 { get; set; }
    public string? SendCmd2 { get; set; }
    public int ConnectTimeout { get; set; } = 3000;
    public bool IsEnabled { get; set; } = true;
    public string? Remark { get; set; }

    public ICollection<IoMappingEntity> IoMappings { get; set; } = new List<IoMappingEntity>();
}