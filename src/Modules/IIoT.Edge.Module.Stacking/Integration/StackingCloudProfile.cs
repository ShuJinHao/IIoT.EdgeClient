using AutoMapper;
using IIoT.Edge.Module.Stacking.Payload;

namespace IIoT.Edge.Module.Stacking.Integration;

/// <summary>
/// 叠片 Cloud 上传字段映射配置，将插件电芯数据转换为云端叠片 DTO。
/// </summary>
public sealed class StackingCloudProfile : Profile
{
    public StackingCloudProfile()
    {
        CreateMap<StackingCellData, StackingCloudDto>()
            .ForMember(
                d => d.CellResult,
                o => o.MapFrom(s => s.CellResult == true
                    ? "OK"
                    : s.CellResult == false
                        ? "NG"
                        : "Unknown"))
            .ForMember(
                d => d.CompletedTime,
                o => o.MapFrom(s => s.CompletedTime.GetValueOrDefault()));
    }
}
