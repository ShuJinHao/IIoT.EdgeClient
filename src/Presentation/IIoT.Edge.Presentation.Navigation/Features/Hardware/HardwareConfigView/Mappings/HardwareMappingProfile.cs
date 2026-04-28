using AutoMapper;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Mappings;

/// <summary>
/// 硬件配置页面映射配置。
/// 负责在硬件配置实体与页面编辑模型之间建立映射关系。
/// </summary>
public class HardwareMappingProfile : Profile
{
    public HardwareMappingProfile()
    {
        // NetworkDeviceEntity <-> NetworkDeviceVm
        CreateMap<NetworkDeviceEntity, NetworkDeviceVm>().ReverseMap();

        // SerialDeviceEntity <-> SerialDeviceVm
        CreateMap<SerialDeviceEntity, SerialDeviceVm>().ReverseMap();

        // IoMappingEntity <-> IoMappingVm
        CreateMap<IoMappingEntity, IoMappingVm>().ReverseMap();
    }
}