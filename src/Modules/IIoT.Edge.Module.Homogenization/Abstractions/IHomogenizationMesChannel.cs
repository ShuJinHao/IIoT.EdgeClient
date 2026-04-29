using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization.Payload;

namespace IIoT.Edge.Module.Homogenization.Abstractions;

/// <summary>
/// 匀浆模块内部的 MES 通道契约。接口留在插件抽象层，避免运行时任务直接依赖 Integration 实现。
/// </summary>
public interface IHomogenizationMesChannel
{
    /// <summary>
    /// 调用 MES 进站校验接口，进站字段和托盘语义仍由匀浆插件负责。
    /// </summary>
    Task<MesCallResult> UploadInboundAsync(
        DeviceSession? device,
        string trayCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 调用 MES 出站上传接口，出站 payload 由匀浆插件的字段映射生成。
    /// </summary>
    Task<MesCallResult> UploadOutboundAsync(
        DeviceSession? device,
        HomogenizationCellData cellData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传匀浆实时数据快照，不把实时数据字段上移到共享层。
    /// </summary>
    Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传匀浆配方快照，配方字段和 MES code 仍由插件配置决定。
    /// </summary>
    Task<MesCallResult> UploadRecipeAsync(
        DeviceSession? device,
        HomogenizationRecipeSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传匀浆设备状态快照，状态码含义仍属于匀浆插件业务。
    /// </summary>
    Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        HomogenizationEquipmentStatusSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
