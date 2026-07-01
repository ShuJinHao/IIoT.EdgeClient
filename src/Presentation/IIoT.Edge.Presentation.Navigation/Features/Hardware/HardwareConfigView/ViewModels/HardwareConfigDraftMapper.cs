using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 硬件配置编辑草稿映射器，集中处理列表项到弹窗草稿之间的复制，避免 ViewModel 重复维护字段映射。
/// </summary>
internal static class HardwareConfigDraftMapper
{
    public static NetworkDeviceVm CloneNetworkDevice(NetworkDeviceVm source)
    {
        var target = new NetworkDeviceVm
        {
            Id = source.Id,
            DeviceName = source.DeviceName,
            DeviceType = source.DeviceType,
            IpAddress = source.IpAddress,
            Port1 = source.Port1,
            Port2 = source.Port2,
            SendCmd1 = source.SendCmd1,
            SendCmd2 = source.SendCmd2,
            ConnectTimeout = source.ConnectTimeout,
            IsEnabled = source.IsEnabled,
            Remark = source.Remark,
            ProtocolFrame = source.ProtocolFrame
        };
        target.DeviceModel = source.DeviceModel;
        return target;
    }

    public static void CopyNetworkDevice(NetworkDeviceVm source, NetworkDeviceVm target)
    {
        target.DeviceName = source.DeviceName;
        target.DeviceType = source.DeviceType;
        target.DeviceModel = source.DeviceModel;
        target.ProtocolFrame = source.ProtocolFrame;
        target.IpAddress = source.IpAddress;
        target.Port1 = source.Port1;
        target.Port2 = source.Port2;
        target.SendCmd1 = source.SendCmd1;
        target.SendCmd2 = source.SendCmd2;
        target.ConnectTimeout = source.ConnectTimeout;
        target.IsEnabled = source.IsEnabled;
        target.Remark = source.Remark;
    }

    public static SerialDeviceVm CloneSerialDevice(SerialDeviceVm source)
        => new()
        {
            Id = source.Id,
            DeviceName = source.DeviceName,
            DeviceType = source.DeviceType,
            PortName = source.PortName,
            BaudRate = source.BaudRate,
            DataBits = source.DataBits,
            StopBits = source.StopBits,
            Parity = source.Parity,
            SendCmd1 = source.SendCmd1,
            SendCmd2 = source.SendCmd2,
            IsEnabled = source.IsEnabled,
            Remark = source.Remark
        };

    public static void CopySerialDevice(SerialDeviceVm source, SerialDeviceVm target)
    {
        target.DeviceName = source.DeviceName;
        target.DeviceType = source.DeviceType;
        target.PortName = source.PortName;
        target.BaudRate = source.BaudRate;
        target.DataBits = source.DataBits;
        target.StopBits = source.StopBits;
        target.Parity = source.Parity;
        target.SendCmd1 = source.SendCmd1;
        target.SendCmd2 = source.SendCmd2;
        target.IsEnabled = source.IsEnabled;
        target.Remark = source.Remark;
    }

    public static IoMappingVm CloneIoMapping(IoMappingVm source)
        => new()
        {
            Id = source.Id,
            NetworkDeviceId = source.NetworkDeviceId,
            SignalKey = source.SignalKey,
            PlcAddress = source.PlcAddress,
            AddressCount = source.AddressCount,
            DataType = source.DataType,
            Direction = source.Direction,
            Category = source.Category,
            BusinessGroup = source.BusinessGroup,
            SortOrder = source.SortOrder,
            Remark = source.Remark
        };

    public static IoInteractionPairDraftVm CloneInteractionPair(IoInteractionPairVm source)
        => new()
        {
            BusinessGroup = source.BusinessGroup,
            ReadPlcAddress = source.ReadPlcAddress,
            ReadAddressCount = source.ReadAddressCount,
            ReadDataType = source.ReadDataType,
            WritePlcAddress = source.WritePlcAddress,
            WriteAddressCount = source.WriteAddressCount,
            WriteDataType = source.WriteDataType,
            Remark = source.Remark
        };

    public static void CopyIoMapping(IoMappingVm source, IoMappingVm target)
    {
        target.PlcAddress = source.PlcAddress;
        target.AddressCount = source.AddressCount;
        target.DataType = source.DataType;
        target.BusinessGroup = source.BusinessGroup;
        target.Remark = source.Remark;
    }

    public static void CopyInteractionPair(IoInteractionPairDraftVm source, IoInteractionPairVm target)
    {
        if (target.ReadMapping is not null)
        {
            target.ReadMapping.PlcAddress = source.ReadPlcAddress;
            target.ReadMapping.AddressCount = source.ReadAddressCount;
            target.ReadMapping.DataType = source.ReadDataType;
            target.ReadMapping.Remark = NormalizeRemark(source.Remark);
        }

        if (target.WriteMapping is not null)
        {
            target.WriteMapping.PlcAddress = source.WritePlcAddress;
            target.WriteMapping.AddressCount = source.WriteAddressCount;
            target.WriteMapping.DataType = source.WriteDataType;
            target.WriteMapping.Remark = NormalizeRemark(source.Remark);
        }
    }

    private static string? NormalizeRemark(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
