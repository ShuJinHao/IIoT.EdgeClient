using IIoT.Edge.SharedKernel.Domain;

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
        Rename(deviceName);
        ChangeDeviceType(deviceType);
        UpdatePort(portName, baudRate, DataBits, StopBits, Parity);
    }

    public string DeviceName { get; private set; } = null!;
    public string DeviceType { get; private set; } = null!;
    public string PortName { get; private set; } = null!;
    public int BaudRate { get; private set; } = 9600;
    public int DataBits { get; private set; } = 8;
    public string StopBits { get; private set; } = "One";
    public string Parity { get; private set; } = "None";
    public string? SendCmd1 { get; private set; }
    public string? SendCmd2 { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public string? Remark { get; private set; }

    public static SerialDeviceEntity Create(
        string deviceName,
        string deviceType,
        string portName,
        int baudRate)
        => new(deviceName, deviceType, portName, baudRate);

    public void Rename(string deviceName)
        => DeviceName = Require(deviceName, "串口设备名称不能为空。");

    public void ChangeDeviceType(string deviceType)
        => DeviceType = Require(deviceType, "串口设备类型不能为空。");

    public void UpdatePort(
        string portName,
        int baudRate,
        int dataBits,
        string stopBits,
        string parity)
    {
        PortName = Require(portName, "串口号不能为空。");
        BaudRate = ValidatePositive(baudRate, "波特率必须大于 0。");
        DataBits = ValidatePositive(dataBits, "数据位必须大于 0。");
        StopBits = Require(stopBits, "停止位不能为空。");
        Parity = Require(parity, "校验位不能为空。");
    }

    public void UpdateCommands(string? sendCmd1, string? sendCmd2)
    {
        SendCmd1 = Normalize(sendCmd1);
        SendCmd2 = Normalize(sendCmd2);
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;

    public void SetEnabled(bool isEnabled)
    {
        if (isEnabled)
        {
            Enable();
            return;
        }

        Disable();
    }

    public void UpdateRemark(string? remark)
        => Remark = Normalize(remark);

    private static string Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message);
        }

        return value.Trim();
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ValidatePositive(int value, string message)
    {
        if (value <= 0)
        {
            throw new ArgumentException(message);
        }

        return value;
    }
}
