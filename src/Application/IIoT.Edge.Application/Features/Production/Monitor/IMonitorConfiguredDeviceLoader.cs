using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Common.Plugins;

namespace IIoT.Edge.Application.Features.Production.Monitor;

/// <summary>
/// 监控页 PLC 配置和任务绑定加载器。
/// </summary>
public interface IMonitorConfiguredDeviceLoader
{
    Task<IReadOnlyList<DevicePluginPlcSnapshot>> LoadConfiguredPlcDevicesAsync(CancellationToken ct);

    Task<IReadOnlyDictionary<int, PlcTaskBindingDeviceDto>> LoadTaskBindingsByDeviceAsync(
        IReadOnlyCollection<DevicePluginPlcSnapshot> configuredPlcs,
        CancellationToken ct);

    bool HasRuntimeFactory(string? moduleId);
}
