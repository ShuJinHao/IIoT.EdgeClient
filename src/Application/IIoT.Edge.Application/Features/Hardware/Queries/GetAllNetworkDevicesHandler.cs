using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Queries;


/// <summary>
/// 处理器：获取全部网络设备配置。
/// </summary>
public class GetAllNetworkDevicesHandler(
    IReadRepository<NetworkDeviceEntity> repo
) : IQueryHandler<GetAllNetworkDevicesQuery, Result<List<NetworkDeviceEntity>>>
{
    public async Task<Result<List<NetworkDeviceEntity>>> Handle(
        GetAllNetworkDevicesQuery request,
        CancellationToken cancellationToken)
    {
        var list = await repo.GetListAsync(_ => true, cancellationToken);
        return Result.Success(list);
    }
}
