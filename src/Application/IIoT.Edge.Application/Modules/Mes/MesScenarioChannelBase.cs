using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Modules.Mes;

/// <summary>
/// MES 多场景通道基类。这里只复用上传骨架和 IProcessMesUploader 适配，不定义任何插件字段映射。
/// </summary>
public abstract class MesScenarioChannelBase<TCellData, TInbound, TRealtime, TRecipe, TEquipmentStatus>
    : MesUploadChannelBase<TCellData>, IMesScenarioChannel<TCellData, TInbound, TRealtime, TRecipe, TEquipmentStatus>
    where TCellData : CellDataBase
{
    protected MesScenarioChannelBase(
        string processType,
        ILogService logger,
        MesRequestExecutor requestExecutor,
        ILocalParameterConfigService parameterConfigService)
        : base(processType, logger, requestExecutor, parameterConfigService)
    {
    }

    public abstract Task<MesCallResult> UploadInboundAsync(
        DeviceSession? device,
        TInbound inbound,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult> UploadOutboundAsync(
        DeviceSession? device,
        TCellData cellData,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        TRealtime snapshot,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult> UploadRecipeAsync(
        DeviceSession? device,
        TRecipe snapshot,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        TEquipmentStatus snapshot,
        CancellationToken cancellationToken = default);
}
