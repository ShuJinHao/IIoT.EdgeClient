using AutoMapper;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;

namespace IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Mappings;

public class HardwareConfigMappingProfile : Profile
{
    public const string NetworkDeviceIdContextKey = "NetworkDeviceId";

    public HardwareConfigMappingProfile()
    {
        CreateMap<NetworkDeviceVm, NetworkDeviceDto>()
            .ConstructUsing(src => new NetworkDeviceDto(
                src.Id,
                src.DeviceName,
                src.DeviceType,
                src.DeviceModel,
                src.ModuleId,
                src.IpAddress,
                src.Port1,
                src.Port2,
                src.SendCmd1,
                src.SendCmd2,
                src.ConnectTimeout,
                src.IsEnabled,
                src.Remark));

        CreateMap<SerialDeviceVm, SerialDeviceDto>()
            .ConstructUsing(src => new SerialDeviceDto(
                src.Id,
                src.DeviceName,
                src.DeviceType,
                src.PortName,
                src.BaudRate,
                src.IsEnabled));

        CreateMap<IoMappingVm, IoMappingDto>()
            .ConstructUsing((src, context) => new IoMappingDto(
                src.Id,
                context.Items.TryGetValue(NetworkDeviceIdContextKey, out var id) ? (int)id : 0,
                src.Label,
                src.PlcAddress,
                src.AddressCount,
                src.DataType,
                src.Direction,
                src.SortOrder));
    }
}
