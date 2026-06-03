using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Identity;

namespace IIoT.Edge.Domain.Hardware.Aggregates;

public class NetworkDeviceEntity : BaseEntity<int>, IAggregateRoot, IDeviceIdentifiable
{
    protected NetworkDeviceEntity() { }

    public NetworkDeviceEntity(
        string deviceName,
        DeviceType deviceType,
        string ipAddress,
        int port1)
    {
        Rename(deviceName);
        ChangeType(deviceType);
        UpdateEndpoint(ipAddress, port1, null, ConnectTimeout);
    }

    public string DeviceName { get; private set; } = null!;
    public DeviceType DeviceType { get; private set; }
    public string? DeviceModel { get; private set; }
    public string IpAddress { get; private set; } = null!;
    public int Port1 { get; private set; }
    public int? Port2 { get; private set; }
    public string? SendCmd1 { get; private set; }
    public string? SendCmd2 { get; private set; }
    public int ConnectTimeout { get; private set; } = 3000;
    public bool IsEnabled { get; private set; } = true;
    public string? Remark { get; private set; }

    public ICollection<IoMappingEntity> IoMappings { get; private set; } = new List<IoMappingEntity>();
    public ICollection<PlcTaskBindingEntity> PlcTaskBindings { get; private set; } = new List<PlcTaskBindingEntity>();

    int IDeviceIdentifiable.NetworkDeviceId => Id;

    public static NetworkDeviceEntity Create(
        string deviceName,
        DeviceType deviceType,
        string ipAddress,
        int port1)
        => new(deviceName, deviceType, ipAddress, port1);

    public void Rename(string deviceName)
        => DeviceName = Require(deviceName, "网络设备名称不能为空。");

    public void ChangeType(DeviceType deviceType)
        => DeviceType = deviceType;

    public void UpdateEndpoint(string ipAddress, int port1, int? port2, int connectTimeout)
    {
        IpAddress = Require(ipAddress, "网络设备地址不能为空。");
        Port1 = ValidatePort(port1, "网络设备主端口必须在 1 到 65535 之间。");
        Port2 = port2.HasValue
            ? ValidatePort(port2.Value, "网络设备备用端口必须在 1 到 65535 之间。")
            : null;
        ConnectTimeout = ValidatePositive(connectTimeout, "网络设备连接超时必须大于 0。");
    }

    public void UpdateDeviceModel(string? deviceModel)
        => DeviceModel = Normalize(deviceModel);

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

    private static int ValidatePort(int value, string message)
    {
        if (value is < 1 or > 65535)
        {
            throw new ArgumentException(message);
        }

        return value;
    }

    private static int ValidatePositive(int value, string message)
    {
        if (value <= 0)
        {
            throw new ArgumentException(message);
        }

        return value;
    }
}
