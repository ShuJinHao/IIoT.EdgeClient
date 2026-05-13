using CommunityToolkit.Mvvm.ComponentModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.HardwareConfig.ViewModels;

public sealed partial class NetworkDeviceRow : ObservableObject
{
    public NetworkDeviceRow(
        int id,
        string deviceName,
        string deviceType,
        string deviceModel,
        string moduleId,
        string ipAddress,
        int port1,
        int port2,
        string sendCmd1,
        string sendCmd2,
        int connectTimeout,
        bool isEnabled,
        string remark)
    {
        Id = id;
        DeviceName = deviceName;
        DeviceType = deviceType;
        DeviceModel = deviceModel;
        ModuleId = moduleId;
        IpAddress = ipAddress;
        Port1 = port1;
        Port2 = port2;
        SendCmd1 = sendCmd1;
        SendCmd2 = sendCmd2;
        ConnectTimeout = connectTimeout;
        IsEnabled = isEnabled;
        Remark = remark;
    }

    public int Id { get; }

    [ObservableProperty]
    private string deviceName;

    [ObservableProperty]
    private string deviceType;

    [ObservableProperty]
    private string deviceModel;

    [ObservableProperty]
    private string moduleId;

    [ObservableProperty]
    private string ipAddress;

    [ObservableProperty]
    private int port1;

    [ObservableProperty]
    private int port2;

    [ObservableProperty]
    private string sendCmd1;

    [ObservableProperty]
    private string sendCmd2;

    [ObservableProperty]
    private int connectTimeout;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private string remark;
}

public sealed partial class SerialDeviceRow : ObservableObject
{
    public SerialDeviceRow(
        int id,
        string deviceName,
        string deviceType,
        string portName,
        int baudRate,
        int dataBits,
        string stopBits,
        string parity,
        string sendCmd1,
        string sendCmd2,
        bool isEnabled,
        string remark)
    {
        Id = id;
        DeviceName = deviceName;
        DeviceType = deviceType;
        PortName = portName;
        BaudRate = baudRate;
        DataBits = dataBits;
        StopBits = stopBits;
        Parity = parity;
        SendCmd1 = sendCmd1;
        SendCmd2 = sendCmd2;
        IsEnabled = isEnabled;
        Remark = remark;
    }

    public int Id { get; }

    [ObservableProperty]
    private string deviceName;

    [ObservableProperty]
    private string deviceType;

    [ObservableProperty]
    private string portName;

    [ObservableProperty]
    private int baudRate;

    [ObservableProperty]
    private int dataBits;

    [ObservableProperty]
    private string stopBits;

    [ObservableProperty]
    private string parity;

    [ObservableProperty]
    private string sendCmd1;

    [ObservableProperty]
    private string sendCmd2;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private string remark;
}

public sealed partial class IoMappingRow : ObservableObject
{
    public IoMappingRow(
        int id,
        int networkDeviceId,
        string plcAddress,
        int addressCount,
        string businessGroup,
        string signalName,
        string dataType,
        string direction,
        bool isAddressCountEditable)
    {
        Id = id;
        NetworkDeviceId = networkDeviceId;
        PlcAddress = plcAddress;
        AddressCount = addressCount;
        BusinessGroup = businessGroup;
        SignalName = signalName;
        DataType = dataType;
        Direction = direction;
        IsAddressCountEditable = isAddressCountEditable;
    }

    public int Id { get; }

    public int NetworkDeviceId { get; }

    [ObservableProperty]
    private string plcAddress;

    [ObservableProperty]
    private int addressCount;

    [ObservableProperty]
    private string businessGroup;

    [ObservableProperty]
    private string signalName;

    [ObservableProperty]
    private string dataType;

    [ObservableProperty]
    private string direction;

    public bool IsAddressCountEditable { get; }
}
