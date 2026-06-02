using MediatR;

namespace IIoT.Edge.Application.Features.Production.Monitor;

/// <summary>
/// 监控快照查询 facade 契约。
/// </summary>
public interface IMonitorSnapshotQueryFacade
{
    Task<List<DeviceMonitorSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 监控快照查询 facade。
/// 负责获取监控面板所需的设备快照列表。
/// </summary>
public sealed class MonitorSnapshotQueryFacade(ISender sender) : IMonitorSnapshotQueryFacade
{
    public Task<List<DeviceMonitorSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
        => sender.Send(new GetMonitorSnapshotQuery(), cancellationToken);
}
