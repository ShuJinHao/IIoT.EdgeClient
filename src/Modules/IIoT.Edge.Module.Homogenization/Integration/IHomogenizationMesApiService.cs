using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization.Payload;

namespace IIoT.Edge.Module.Homogenization.Integration;

/// <summary>
/// 匀浆模块访问 MES 的工序接口集合。
/// </summary>
public interface IHomogenizationMesApiService
{
    /// <summary>
    /// 上传进站托盘码校验请求。
    /// </summary>
    Task<MesCallResult> UploadInboundAsync(
        DeviceSession? device,
        string trayCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传出料完成后的完整电芯数据。
    /// </summary>
    Task<MesCallResult> UploadOutboundAsync(
        DeviceSession? device,
        HomogenizationCellData cellData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传 PLC 周期采集的匀浆实时快照。
    /// </summary>
    Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传 PLC 触发时读取的配方参数快照。
    /// </summary>
    Task<MesCallResult> UploadRecipeAsync(
        DeviceSession? device,
        HomogenizationRecipeSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传 PLC 触发时读取的设备状态快照。
    /// </summary>
    Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        HomogenizationEquipmentStatusSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
