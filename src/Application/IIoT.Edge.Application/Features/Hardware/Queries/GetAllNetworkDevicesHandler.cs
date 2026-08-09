using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Application.Features.Hardware.Queries;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Queries;


/// <summary>
/// 处理器：获取全部网络设备配置。
/// </summary>
public class GetAllNetworkDevicesHandler(
    IDevicePluginConfigurationSnapshotAccessor snapshots
) : IQueryHandler<GetAllNetworkDevicesQuery, Result<List<DevicePluginPlcSnapshot>>>
{
    public Task<Result<List<DevicePluginPlcSnapshot>>> Handle(
        GetAllNetworkDevicesQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success(snapshots.GetPlcs().ToList()));
    }
}
