using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Queries;


/// <summary>
/// 处理器：分页获取指定网络设备的 IO 映射。
/// </summary>
public class GetIoMappingsByDeviceHandler(
    IReadRepository<IoMappingEntity> repo
) : IQueryHandler<GetIoMappingsByDeviceQuery, Result<IoMappingPagedDto>>
{
    public async Task<Result<IoMappingPagedDto>> Handle(
        GetIoMappingsByDeviceQuery request,
        CancellationToken cancellationToken)
    {
        var all = await repo.GetListAsync(
            x => x.NetworkDeviceId == request.NetworkDeviceId,
            cancellationToken);

        var totalCount = all.Count;
        var items = all
            .OrderBy(x => x.SortOrder)
            .Skip(request.PageIndex * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return Result.Success(new IoMappingPagedDto(items, totalCount));
    }
}
