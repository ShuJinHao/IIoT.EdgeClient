using System;
using AutoMapper;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Infrastructure.Integration.Mappings.Cloud.Injection;

/// <summary>
/// 注液机 -> 云端 DTO 的 AutoMapper Profile
/// 
/// InjectionCellData -> InjectionCloudDto
/// bool? CellResult -> string "OK"/"NG"
/// 时间字段在业务尚未最终确定前使用兜底映射：
///   CompletedTime 优先用完成时间，无值时用当前时间
///   PreInjectionTime 优先用扫码时间，无值时回退 CompletedTime
///   PostInjectionTime 优先用完成时间，无值时回退扫码时间
/// </summary>
public class InjectionCloudProfile : Profile
{
    public InjectionCloudProfile()
    {
        CreateMap<InjectionCellData, InjectionCloudDto>()
            .ForMember(d => d.CellResult,
                o => o.MapFrom(s => s.CellResult == true ? "OK" : "NG"))
            .ForMember(d => d.CompletedTime,
                o => o.MapFrom(s => s.CompletedTime ?? DateTime.UtcNow))
            .ForMember(d => d.PreInjectionTime,
                o => o.MapFrom(s => s.ScanTime ?? s.CompletedTime ?? DateTime.UtcNow))
            .ForMember(d => d.PostInjectionTime,
                o => o.MapFrom(s => s.CompletedTime ?? s.ScanTime ?? DateTime.UtcNow));
    }
}
