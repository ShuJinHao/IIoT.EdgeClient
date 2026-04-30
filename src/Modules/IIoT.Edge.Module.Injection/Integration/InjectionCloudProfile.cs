using AutoMapper;
using IIoT.Edge.Module.Injection.Payload;

namespace IIoT.Edge.Module.Injection.Integration;

/// <summary>
/// 注液云端 DTO 映射。上传器会按统一生产时区处理时间兜底，本配置只保留静态字段映射。
/// </summary>
public class InjectionCloudProfile : Profile
{
    /// <summary>
    /// 建立注液电芯数据到云端 DTO 的字段映射规则。
    /// </summary>
    public InjectionCloudProfile()
    {
        CreateMap<InjectionCellData, InjectionCloudDto>()
            .ForMember(
                d => d.CellResult,
                o => o.MapFrom(s => s.CellResult == true ? "OK" : "NG"))
            .ForMember(
                d => d.CompletedTime,
                o => o.MapFrom(s => s.CompletedTime.GetValueOrDefault()))
            .ForMember(
                d => d.PreInjectionTime,
                o => o.MapFrom(s => (s.ScanTime ?? s.CompletedTime).GetValueOrDefault()))
            .ForMember(
                d => d.PostInjectionTime,
                o => o.MapFrom(s => (s.CompletedTime ?? s.ScanTime).GetValueOrDefault()));
    }
}
