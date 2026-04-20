using AutoMapper;
using IIoT.Edge.Module.Injection.Payload;

namespace IIoT.Edge.Module.Injection.Integration;

public class InjectionCloudProfile : Profile
{
    public InjectionCloudProfile()
    {
        CreateMap<InjectionCellData, InjectionCloudDto>()
            .ForMember(
                d => d.CellResult,
                o => o.MapFrom(s => s.CellResult == true ? "OK" : "NG"))
            .ForMember(
                d => d.CompletedTime,
                o => o.MapFrom(s => s.CompletedTime ?? DateTime.UtcNow))
            .ForMember(
                d => d.PreInjectionTime,
                o => o.MapFrom(s => s.ScanTime ?? s.CompletedTime ?? DateTime.UtcNow))
            .ForMember(
                d => d.PostInjectionTime,
                o => o.MapFrom(s => s.CompletedTime ?? s.ScanTime ?? DateTime.UtcNow));
    }
}
