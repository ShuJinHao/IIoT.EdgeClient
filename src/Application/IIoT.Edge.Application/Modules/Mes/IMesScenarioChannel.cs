using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Modules.Mes;

/// <summary>
/// MES 多场景通道契约。Application 只定义场景形态，不持有任何插件业务字段或 MES code。
/// </summary>
public interface IMesScenarioChannel<
    TCellData,
    TInbound,
    TRealtime,
    TRecipe,
    TEquipmentStatus,
    TMainPlanRequest,
    TMainPlanResult,
    TTraceBatchRequest,
    TTraceBatchResult> : IProcessMesUploader
    where TCellData : CellDataBase
{
    /// <summary>
    /// 上传进站校验数据。具体托盘、扫码或工单语义由插件的 TInbound 决定。
    /// </summary>
    Task<MesCallResult> UploadInboundAsync(
        DeviceSession? device,
        TInbound inbound,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传出站/出料完成数据。字段映射和 MES code 仍由插件实现。
    /// </summary>
    Task<MesCallResult> UploadOutboundAsync(
        DeviceSession? device,
        TCellData cellData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传实时数据快照。实时字段集合由插件自己的快照类型承载。
    /// </summary>
    Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        TRealtime snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传配方/工艺参数快照。配方字段集合由插件自己的快照类型承载。
    /// </summary>
    Task<MesCallResult> UploadRecipeAsync(
        DeviceSession? device,
        TRecipe snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传设备状态快照。状态码和状态文案仍由插件配置决定。
    /// </summary>
    Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        TEquipmentStatus snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 MES 主批计划。请求/响应字段由具体工序插件定义。
    /// </summary>
    Task<MesCallResult<TMainPlanResult>> GetMainPlanAsync(
        TMainPlanRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成 MES 追溯批次号。请求/响应字段由具体工序插件定义。
    /// </summary>
    Task<MesCallResult<TTraceBatchResult>> GenerateTraceBatchNumberAsync(
        TTraceBatchRequest request,
        CancellationToken cancellationToken = default);
}
