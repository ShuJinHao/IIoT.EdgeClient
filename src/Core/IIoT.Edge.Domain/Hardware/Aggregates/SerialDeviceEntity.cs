using IIoT.Edge.Common.Domain;

namespace IIoT.Edge.Domain.Hardware.Aggregates;

public class SerialDeviceEntity : BaseEntity<int>, IAggregateRoot
{
    protected SerialDeviceEntity() { }

    public SerialDeviceEntity(
        string deviceName,
        string deviceType,
        string portName,
        int baudRate)
    {
        DeviceName = deviceName;
        DeviceType = deviceType;
        PortName = portName;
        BaudRate = baudRate;
    }

    public string DeviceName { get; set; } = null!;
    public string DeviceType { get; set; } = null!;
    public string PortName { get; set; } = null!;
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public string StopBits { get; set; } = "One";
    public string Parity { get; set; } = "None";
    public string? SendCmd1 { get; set; }
    public string? SendCmd2 { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? Remark { get; set; }
}