using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public interface IHardwareConfigEditModelMapper
{
    NetworkDeviceVm ToNetworkDeviceVm(NetworkDeviceDto dto);

    SerialDeviceVm ToSerialDeviceVm(SerialDeviceDto dto);

    IoMappingVm ToIoMappingVm(IoMappingDto dto);

    NetworkDeviceDto ToNetworkDeviceDto(NetworkDeviceVm model);

    SerialDeviceDto ToSerialDeviceDto(SerialDeviceVm model);

    IoMappingDto ToIoMappingDto(IoMappingVm model, int networkDeviceId);
}

public sealed class HardwareConfigEditModelMapper : IHardwareConfigEditModelMapper
{
    public NetworkDeviceVm ToNetworkDeviceVm(NetworkDeviceDto dto)
        => new()
        {
            Id = dto.Id,
            DeviceName = dto.DeviceName,
            DeviceType = dto.DeviceType,
            DeviceModel = dto.DeviceModel,
            ProtocolFrame = dto.ProtocolFrame,
            IpAddress = dto.IpAddress,
            Port1 = dto.Port1,
            Port2 = dto.Port2,
            SendCmd1 = dto.SendCmd1,
            SendCmd2 = dto.SendCmd2,
            ConnectTimeout = dto.ConnectTimeout,
            IsEnabled = dto.IsEnabled,
            Remark = dto.Remark
        };

    public SerialDeviceVm ToSerialDeviceVm(SerialDeviceDto dto)
        => new()
        {
            Id = dto.Id,
            DeviceName = dto.DeviceName,
            DeviceType = dto.DeviceType,
            PortName = dto.PortName,
            BaudRate = dto.BaudRate,
            DataBits = dto.DataBits,
            StopBits = dto.StopBits,
            Parity = dto.Parity,
            SendCmd1 = dto.SendCmd1,
            SendCmd2 = dto.SendCmd2,
            IsEnabled = dto.IsEnabled,
            Remark = dto.Remark
        };

    public IoMappingVm ToIoMappingVm(IoMappingDto dto)
        => new()
        {
            Id = dto.Id,
            NetworkDeviceId = dto.NetworkDeviceId,
            SignalKey = dto.SignalKey,
            PlcAddress = dto.PlcAddress,
            AddressCount = dto.AddressCount,
            DataType = dto.DataType,
            Direction = dto.Direction,
            Category = dto.Category,
            BusinessGroup = dto.BusinessGroup,
            SortOrder = dto.SortOrder,
            Remark = dto.Remark
        };

    public NetworkDeviceDto ToNetworkDeviceDto(NetworkDeviceVm model)
        => new(
            model.Id,
            model.DeviceName,
            model.DeviceType,
            model.DeviceModel,
            model.IpAddress,
            model.Port1,
            model.Port2,
            model.SendCmd1,
            model.SendCmd2,
            model.ConnectTimeout,
            model.IsEnabled,
            model.Remark,
            model.ProtocolFrame);

    public SerialDeviceDto ToSerialDeviceDto(SerialDeviceVm model)
        => new(
            model.Id,
            model.DeviceName,
            model.DeviceType,
            model.PortName,
            model.BaudRate,
            model.DataBits,
            model.StopBits,
            model.Parity,
            model.SendCmd1,
            model.SendCmd2,
            model.IsEnabled,
            model.Remark);

    public IoMappingDto ToIoMappingDto(IoMappingVm model, int networkDeviceId)
        => new(
            model.Id,
            networkDeviceId,
            model.SignalKey,
            model.PlcAddress,
            model.AddressCount,
            model.DataType,
            model.Direction,
            model.Category,
            model.BusinessGroup,
            model.SortOrder,
            model.Remark);
}
