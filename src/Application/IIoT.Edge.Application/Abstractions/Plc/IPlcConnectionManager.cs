using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.Application.Abstractions.Plc.Store;

namespace IIoT.Edge.Application.Abstractions.Plc;

/// <summary>
/// PLC 连接管理器契约。
/// 负责 PLC 连接建立、任务调度、缓冲区初始化与设备热重载。
/// </summary>
public interface IPlcConnectionManager : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// 启动时初始化所有已启用的 PLC 设备。
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// 停止所有 PLC 后台任务并释放连接资源。
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// 配置保存后热重载指定设备，不影响其他设备上下文。
    /// </summary>
    Task ReloadAsync(string deviceName, CancellationToken ct = default);

    /// <summary>
    /// 按设备 Id 停止单个 PLC 设备及其后台任务。
    /// </summary>
    Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default);

    /// <summary>
    /// 按设备名注册任务工厂，供机台专属模块在启动时调用。
    /// </summary>
    void RegisterTasks(
        string deviceName,
        Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory);

    /// <summary>
    /// 获取指定设备的 PLC 通信实例。
    /// </summary>
    IPlcService? GetPlc(int networkDeviceId);

    /// <summary>
    /// 获取指定设备的生产上下文。
    /// </summary>
    ProductionContext? GetContext(string deviceName);
}
