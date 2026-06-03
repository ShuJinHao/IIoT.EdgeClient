using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.SharedKernel.Identity;

namespace IIoT.Edge.Application.Features.Production.Monitor;

/// <summary>
/// 监控快照来源匹配器，负责在运行上下文、PLC 运行状态和配置设备之间建立对应关系。
/// </summary>
public interface IMonitorSnapshotSourceMatcher
{
    IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses();

    PlcConnectionRuntimeSnapshot? ResolveRuntimeStatus(IDeviceIdentifiable source);

    T? ResolveConfiguredDevice<T>(IDeviceIdentifiable source, IReadOnlyList<T> devices)
        where T : class, IDeviceIdentifiable;
}
