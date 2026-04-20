using AutoMapper;
using IIoT.Edge.Module.Stacking.Payload;

namespace IIoT.Edge.Module.Stacking.Integration;

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
                o => o.MapFrom(s => s.CompletedTime ?? DateTime.UtcNow));
    }
}
