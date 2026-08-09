using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Application.Features.Hardware.Queries;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Queries;


/// <summary>
/// 处理器：分页获取指定网络设备的 IO 映射。
/// </summary>
public class GetIoMappingsByDeviceHandler(
    IDevicePluginConfigurationSnapshotAccessor snapshots
) : IQueryHandler<GetIoMappingsByDeviceQuery, Result<IoMappingPagedDto>>
{
    public Task<Result<IoMappingPagedDto>> Handle(
        GetIoMappingsByDeviceQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var all = snapshots.GetIoPoints()
            .Where(x => x.NetworkDeviceId == request.NetworkDeviceId)
            .ToArray();

        var totalCount = all.Length;
        var items = all
            .OrderBy(x => x.SortOrder)
            .Skip(request.PageIndex * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return Task.FromResult(Result.Success(new IoMappingPagedDto(items, totalCount)));
    }
}
