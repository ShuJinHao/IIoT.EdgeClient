using AutoMapper;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Infrastructure.Integration.Mappings.Cloud.Stacking;

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
                o => o.MapFrom(s => s.CompletedTime ?? DateTime.Now));
    }
}
