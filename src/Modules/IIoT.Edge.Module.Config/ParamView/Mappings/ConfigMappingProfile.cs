using AutoMapper;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.Module.Config.ParamView.Models;

namespace IIoT.Edge.Module.Config.ParamView.Mappings;

public class ConfigMappingProfile : Profile
{
    public ConfigMappingProfile()
    {
        // ── SystemConfig ↔ GeneralParamVm ──
        // 只保留 Entity -> VM 的正向映射（用于界面读取展示）
        CreateMap<SystemConfigEntity, GeneralParamVm>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Key))
            .ForMember(d => d.Value, o => o.MapFrom(s => s.Value))
            .ForMember(d => d.Description,
                o => o.MapFrom(s => s.Description ?? ""));

        // 注意：已彻底删除 GeneralParamVm -> SystemConfigEntity 的映射，避免调用保护级别构造函数

        // ── DeviceParam ↔ DeviceParamVm ──
        // 只保留 Entity -> VM 的正向映射（用于界面读取展示）
        CreateMap<DeviceParamEntity, DeviceParamVm>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Name))
            .ForMember(d => d.Value, o => o.MapFrom(s => s.Value))
            .ForMember(d => d.Unit, o => o.MapFrom(s => s.Unit ?? ""))
            .ForMember(d => d.Min, o => o.MapFrom(s => s.MinValue ?? ""))
            .ForMember(d => d.Max, o => o.MapFrom(s => s.MaxValue ?? ""));

        // 注意：已彻底删除 DeviceParamVm -> DeviceParamEntity 的映射，避免调用保护级别构造函数
    }
}