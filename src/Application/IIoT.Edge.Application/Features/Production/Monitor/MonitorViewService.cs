using MediatR;

namespace IIoT.Edge.Application.Features.Production.Monitor;

/// <summary>
/// 监控页面服务契约。
/// </summary>
public interface IMonitorViewService
{
    Task<List<DeviceMonitorSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 监控页面服务。
/// 负责获取监控面板所需的设备快照列表。
/// </summary>
public sealed class MonitorViewService(ISender sender) : IMonitorViewService
{
    public Task<List<DeviceMonitorSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
        => sender.Send(new GetMonitorSnapshotQuery(), cancellationToken);
}
