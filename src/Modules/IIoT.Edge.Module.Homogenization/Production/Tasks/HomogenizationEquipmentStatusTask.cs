using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Production.Tasks;

/// <summary>
/// 设备状态握手任务：PLC 触发后读取状态码并按配置进入上传链路。
/// </summary>
internal sealed class HomogenizationEquipmentStatusTask : HomogenizationTaskBase
{
    private readonly IDataPipelineService _dataPipelineService;
    private readonly IMesUploadDiagnosticsStore _mesDiagnosticsStore;
    private readonly ICloudUploadDiagnosticsStore _cloudDiagnosticsStore;
    private readonly ICloudExecutionPolicy _cloudExecutionPolicy;
    private readonly IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> _parameters;

    /// <summary>
    /// 创建匀浆设备状态上传握手任务。
    /// </summary>
    public HomogenizationEquipmentStatusTask(
        IPlcBuffer buffer,
        HomogenizationPlcHandshakeAccessor interaction,
        HomogenizationSignalCodec codec,
        HomogenizationContext context,
        IDataPipelineService dataPipelineService,
        IMesUploadDiagnosticsStore mesDiagnosticsStore,
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
        ICloudExecutionPolicy cloudExecutionPolicy,
        IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> parameters,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, interaction, codec, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _dataPipelineService = dataPipelineService;
        _mesDiagnosticsStore = mesDiagnosticsStore;
        _cloudDiagnosticsStore = cloudDiagnosticsStore;
        _cloudExecutionPolicy = cloudExecutionPolicy;
        _parameters = parameters;
    }

    /// <summary>
    /// 设备状态上传任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.EquipmentStatus";

    protected override async Task DoCoreAsync()
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.设备状态上传;

        await ExecuteHandshakeAsync(
            trigger,
            "设备状态上传触发。",
            "设备状态上传复位。",
            ProcessTriggerAsync,
            static ex => $"设备状态上传处理异常：{ex.Message}",
            message =>
            {
                ModuleContext.LastEquipmentStatusAt = ProductionTime.BusinessNow;
                ModuleContext.LastEquipmentStatusResult = message;
                _mesDiagnosticsStore.RecordFailure(
                    CodeOptions.Mes.Channels.EquipmentStatus,
                    message,
                    CreateMesDiagnosticsContext("设备状态上传"));
            }).ConfigureAwait(false);
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.设备状态上传;
        var snapshot = Codec.CaptureEquipmentStatusSnapshot(CodeOptions.Mes);

        var (result, uploadTargets) = await EnqueueEquipmentStatusAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            RecordUploadDiagnostics(result, uploadTargets);
        }

        ModuleContext.LastEquipmentStatusAt = snapshot.CapturedAt;
        ModuleContext.LastEquipmentStatusResult = result.Message;
        ModuleContext.LastEquipmentStatusSnapshot = snapshot;
        Interaction.ReplyResult(trigger, result);
    }

    private async Task<(MesCallResult Result, DataPipelineUploadTargets UploadTargets)> EnqueueEquipmentStatusAsync(
        HomogenizationEquipmentStatusSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var parameterSnapshot = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        var uploadTargets = ResolveUploadTargets(
            parameterSnapshot.Mes<bool>(HomogenizationParams.Mes.启用),
            _cloudExecutionPolicy.IsEnabled);
        if (uploadTargets == DataPipelineUploadTargets.None)
        {
            return (MesCallResult.Disabled("MES/Cloud 上传已关闭，设备状态上传已跳过。"), uploadTargets);
        }

        var cellData = new HomogenizationCellData
        {
            RecordKind = HomogenizationCellData.RecordKindEquipmentStatus,
            DeviceName = ModuleContext.DeviceName,
            DeviceCode = ModuleContext.DeviceName,
            PlcDeviceId = ModuleContext.NetworkDeviceId,
            CompletedTime = snapshot.CapturedAt,
            RuntimeStatus = "设备状态待上传",
            EquipmentStatusSnapshot = snapshot,
            UploadTargets = uploadTargets
        };

        try
        {
            var enqueueResult = await _dataPipelineService
                .EnqueueAsync(CreatePipelineRecord(cellData, includeMesPlanContext: false), cancellationToken)
                .ConfigureAwait(false);

            return (ToMesQueueResult(
                    enqueueResult,
                    $"设备状态已进入 {FormatUploadTargets(uploadTargets)} 上传队列。",
                    "设备状态已接收，数据已进入溢出持久化。",
                    "设备状态未接收，数据管道拒绝入队"),
                uploadTargets);
        }
        catch (Exception ex)
        {
            return (MesCallResult.TransportFailure($"设备状态处理异常：{ex.Message}"), uploadTargets);
        }
    }

    private void RecordUploadDiagnostics(
        MesCallResult result,
        DataPipelineUploadTargets uploadTargets)
        => RecordUploadFailureDiagnostics(
            result.Message,
            uploadTargets,
            _mesDiagnosticsStore,
            _cloudDiagnosticsStore,
            CodeOptions.Mes.Channels.EquipmentStatus,
            "plc_equipment_status_enqueue_failed",
            "设备状态上传");
}
