using AutoMapper;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Module.Hardware.HardwareConfigView.Mappings;

public class HardwareMappingProfile : Profile
{
    public HardwareMappingProfile()
    {
        // NetworkDevice
        CreateMap<NetworkDeviceEntity, NetworkDeviceVm>().ReverseMap();

        // SerialDevice
        CreateMap<SerialDeviceEntity, SerialDeviceVm>().ReverseMap();

        // IoMapping
        CreateMap<IoMappingEntity, IoMappingVm>().ReverseMap();
    }
}