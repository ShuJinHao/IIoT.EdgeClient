using CommunityToolkit.Mvvm.ComponentModel;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Modules.Hardware;
using DeviceTypeEnum = IIoT.Edge.SharedKernel.Enums.DeviceType;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.HardwareConfig.ViewModels;

public sealed partial class NetworkDeviceRow : ObservableObject
{
    public NetworkDeviceRow(NetworkDeviceVm source)
        : this(
            source.Id,
            source.DeviceName,
            source.DeviceType.ToString(),
            source.DeviceModel ?? string.Empty,
            source.ModuleId,
            source.IpAddress,
            source.Port1,
            source.Port2 ?? 0,
            source.SendCmd1 ?? string.Empty,
            source.SendCmd2 ?? string.Empty,
            source.ConnectTimeout,
            source.IsEnabled,
            source.Remark ?? string.Empty)
    {
    }

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

    public NetworkDeviceVm ToVm()
        => new()
        {
            Id = Id,
            DeviceName = DeviceName,
            DeviceType = Enum.TryParse<DeviceTypeEnum>(DeviceType, true, out var deviceType) ? deviceType : DeviceTypeEnum.PLC,
            DeviceModel = string.IsNullOrWhiteSpace(DeviceModel) ? null : DeviceModel,
            ModuleId = ModuleId,
            IpAddress = IpAddress,
            Port1 = Port1,
            Port2 = Port2 <= 0 ? null : Port2,
            SendCmd1 = string.IsNullOrWhiteSpace(SendCmd1) ? null : SendCmd1,
            SendCmd2 = string.IsNullOrWhiteSpace(SendCmd2) ? null : SendCmd2,
            ConnectTimeout = ConnectTimeout,
            IsEnabled = IsEnabled,
            Remark = string.IsNullOrWhiteSpace(Remark) ? null : Remark
        };
}

public sealed partial class SerialDeviceRow : ObservableObject
{
    public SerialDeviceRow(SerialDeviceVm source)
        : this(
            source.Id,
            source.DeviceName,
            source.DeviceType,
            source.PortName,
            source.BaudRate,
            source.DataBits,
            source.StopBits,
            source.Parity,
            source.SendCmd1 ?? string.Empty,
            source.SendCmd2 ?? string.Empty,
            source.IsEnabled,
            source.Remark ?? string.Empty)
    {
    }

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

    public SerialDeviceVm ToVm()
        => new()
        {
            Id = Id,
            DeviceName = DeviceName,
            DeviceType = DeviceType,
            PortName = PortName,
            BaudRate = BaudRate,
            DataBits = DataBits,
            StopBits = StopBits,
            Parity = Parity,
            SendCmd1 = string.IsNullOrWhiteSpace(SendCmd1) ? null : SendCmd1,
            SendCmd2 = string.IsNullOrWhiteSpace(SendCmd2) ? null : SendCmd2,
            IsEnabled = IsEnabled,
            Remark = string.IsNullOrWhiteSpace(Remark) ? null : Remark
        };
}

public sealed partial class IoMappingRow : ObservableObject
{
    public IoMappingRow(IoMappingVm source)
        : this(
            source.Id,
            source.NetworkDeviceId,
            source.SignalKey,
            source.PlcAddress,
            source.AddressCount,
            source.Category,
            source.BusinessGroup,
            source.SignalName,
            source.DataType,
            source.Direction,
            source.SortOrder,
            source.Remark,
            source.IsAddressCountEditable)
    {
    }

    public IoMappingRow(int id, int networkDeviceId, ModuleIoTemplateEntry source)
        : this(
            id,
            networkDeviceId,
            source.SignalKey,
            source.PlcAddress,
            source.AddressCount,
            source.Category,
            source.BusinessGroup,
            source.SignalName,
            source.DataType,
            source.Direction,
            source.SortOrder,
            source.Remark,
            source.AddressCount > 1)
    {
    }

    public IoMappingRow(
        int id,
        int networkDeviceId,
        string signalKey,
        string plcAddress,
        int addressCount,
        string category,
        string businessGroup,
        string signalName,
        string dataType,
        string direction,
        int sortOrder,
        string? remark,
        bool isAddressCountEditable)
    {
        Id = id;
        NetworkDeviceId = networkDeviceId;
        SignalKey = signalKey;
        PlcAddress = plcAddress;
        AddressCount = addressCount;
        Category = category;
        BusinessGroup = businessGroup;
        SignalName = signalName;
        DataType = dataType;
        Direction = direction;
        SortOrder = sortOrder;
        Remark = remark ?? string.Empty;
        IsAddressCountEditable = isAddressCountEditable;
    }

    public int Id { get; }

    public int NetworkDeviceId { get; }

    [ObservableProperty]
    private string signalKey;

    [ObservableProperty]
    private string plcAddress;

    [ObservableProperty]
    private int addressCount;

    [ObservableProperty]
    private string category;

    [ObservableProperty]
    private string businessGroup;

    [ObservableProperty]
    private string signalName;

    [ObservableProperty]
    private string dataType;

    [ObservableProperty]
    private string direction;

    [ObservableProperty]
    private int sortOrder;

    [ObservableProperty]
    private string remark;

    public bool IsAddressCountEditable { get; }

    public IoMappingVm ToVm()
        => new()
        {
            Id = Id,
            NetworkDeviceId = NetworkDeviceId,
            SignalKey = SignalKey,
            PlcAddress = PlcAddress,
            AddressCount = AddressCount,
            Category = Category,
            BusinessGroup = BusinessGroup,
            SignalName = SignalName,
            DataType = DataType,
            Direction = Direction,
            SortOrder = SortOrder,
            Remark = string.IsNullOrWhiteSpace(Remark) ? null : Remark
        };
}

public sealed class IoMappingCandidateRow
{
    public IoMappingCandidateRow(ModuleIoTemplateEntry source)
    {
        Source = source;
    }

    public ModuleIoTemplateEntry Source { get; }

    public string DisplayText => string.IsNullOrWhiteSpace(Source.SignalName)
        ? $"{Source.SignalKey} / {Source.Direction} / {Source.PlcAddress}"
        : $"{Source.SignalName} / {Source.Direction} / {Source.PlcAddress}";
}
