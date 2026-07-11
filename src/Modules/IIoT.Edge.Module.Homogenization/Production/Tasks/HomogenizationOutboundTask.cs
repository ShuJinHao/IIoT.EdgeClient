using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Resources;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.Module.Sdk.Diagnostics;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Module.Homogenization.Production.Tasks;

/// <summary>
/// 出料握手任务：PLC 触发后读取出料数据并写入本地数据管道。
/// </summary>
internal sealed class HomogenizationOutboundTask : HomogenizationTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly IDataPipelineService _dataPipelineService;
    private readonly HomogenizationCellDataValidator _validator;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly ICloudUploadDiagnosticsStore _cloudDiagnosticsStore;
    private readonly ICloudExecutionPolicy _cloudExecutionPolicy;
    private readonly IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> _parameters;
    private readonly IHomogenizationProductionGate _productionGate;

    /// <summary>
    /// 创建匀浆出料握手任务。
    /// </summary>
    public HomogenizationOutboundTask(
        IPlcBuffer buffer,
        HomogenizationPlcHandshakeAccessor interaction,
        HomogenizationSignalCodec codec,
        HomogenizationContext context,
        IDeviceService deviceService,
        IDataPipelineService dataPipelineService,
        HomogenizationCellDataValidator validator,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
        ICloudExecutionPolicy cloudExecutionPolicy,
        IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> parameters,
        IHomogenizationProductionGate productionGate,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, interaction, codec, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _deviceService = deviceService;
        _dataPipelineService = dataPipelineService;
        _validator = validator;
        _diagnosticsStore = diagnosticsStore;
        _cloudDiagnosticsStore = cloudDiagnosticsStore;
        _cloudExecutionPolicy = cloudExecutionPolicy;
        _parameters = parameters;
        _productionGate = productionGate;
    }

    /// <summary>
    /// 出料握手任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Outbound";

    protected override async Task DoCoreAsync()
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.出料上传;

        await ExecuteHandshakeAsync(
            trigger,
            "出料上传已触发。",
            "出料复位。",
            ProcessTriggerAsync,
            static ex => $"出料处理异常：{ex.Message}",
            message =>
            {
                ModuleContext.LastOutboundAt = ProductionTime.BusinessNow;
                ModuleContext.LastOutboundResult = message;
                ModuleUploadDiagnosticsRecorder.RecordFailure(
                    message,
                    DataPipelineUploadTargets.Mes,
                    _diagnosticsStore,
                    _cloudDiagnosticsStore,
                    new ModuleUploadDiagnosticsRoute(
                        CodeOptions.Mes.Channels.Outbound,
                        DependencyInjection.ModuleKey,
                        "plc_outbound_blocked",
                        "plc_outbound_exception"),
                    new ModuleUploadDiagnosticsIdentity(
                        ModuleContext.DeviceName,
                        DependencyInjection.ModuleKey,
                        TaskName,
                        "出站上传"));
            }).ConfigureAwait(false);
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.出料上传;
        var parameterSnapshot = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        var mesEnabled = parameterSnapshot.Mes<bool>(HomogenizationParams.Mes.启用);
        var cloudEnabled = _cloudExecutionPolicy.IsEnabled;
        var uploadTargets = ResolveUploadTargets(mesEnabled, cloudEnabled);
        var diagnosticsIdentity = new ModuleUploadDiagnosticsIdentity(
            ModuleContext.DeviceName,
            DependencyInjection.ModuleKey,
            TaskName,
            "出站上传");

        if (mesEnabled)
        {
            var gateResult = await _productionGate.EnsureReadyAsync(ModuleContext, cancellationToken).ConfigureAwait(false);
            if (!gateResult.IsSuccess)
            {
                RecordOutboundResult(gateResult.Message);
                ModuleUploadDiagnosticsRecorder.RecordBlocked(
                    gateResult.Message,
                    uploadTargets,
                    _diagnosticsStore,
                    _cloudDiagnosticsStore,
                    new ModuleUploadDiagnosticsRoute(
                        CodeOptions.Mes.Channels.Outbound,
                        DependencyInjection.ModuleKey,
                        "plc_outbound_blocked",
                        "plc_outbound_enqueue_failed"),
                    diagnosticsIdentity);
                Interaction.ReplyResult(trigger, gateResult);
                return;
            }
        }

        var cellData = BuildRecord(uploadTargets);
        if (!_validator.TryValidate(cellData, out var error))
        {
            var message = error ?? "出料校验失败。";
            RecordOutbound(cellData, message);
            ModuleUploadDiagnosticsRecorder.RecordFailure(
                message,
                uploadTargets,
                _diagnosticsStore,
                _cloudDiagnosticsStore,
                new ModuleUploadDiagnosticsRoute(
                    CodeOptions.Mes.Channels.Outbound,
                    DependencyInjection.ModuleKey,
                    "plc_outbound_blocked",
                    "plc_outbound_validation_failed"),
                diagnosticsIdentity);
            Interaction.ReplyException(trigger);
            return;
        }

        var duplicateMessage = await ResolveDuplicateTrayMessageAsync(
            _parameters,
            HomogenizationTrayCodeStage.Outbound,
            cellData.TrayCode,
            cancellationToken).ConfigureAwait(false);
        if (duplicateMessage is not null)
        {
            Interaction.ReplyMesNg(trigger);
            RecordOutbound(cellData, duplicateMessage);
            ModuleUploadDiagnosticsRecorder.RecordFailure(
                duplicateMessage,
                uploadTargets,
                _diagnosticsStore,
                _cloudDiagnosticsStore,
                new ModuleUploadDiagnosticsRoute(
                    CodeOptions.Mes.Channels.Outbound,
                    DependencyInjection.ModuleKey,
                    "plc_outbound_blocked",
                    "plc_outbound_duplicate_tray"),
                diagnosticsIdentity);
            return;
        }

        if (uploadTargets == DataPipelineUploadTargets.None)
        {
            var localOnlyResult = "MES/Cloud 上传已关闭，出料已本地记录。";
            ModuleContext.MarkProcessedTray(
                HomogenizationTrayCodeStage.Outbound,
                cellData.TrayCode,
                "出站已本地记录",
                cellData.CompletedTime ?? ProductionTime.BusinessNow);
            RecordOutbound(cellData, localOnlyResult);
            Interaction.ReplyOk(trigger);
            return;
        }

        DataPipelineEnqueueResult enqueueResult;
        try
        {
            enqueueResult = await _dataPipelineService
                .EnqueueAsync(CreatePipelineRecord(cellData, includeMesPlanContext: mesEnabled), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var message = $"出料处理异常：{ex.Message}";
            RecordOutboundResult(message);
            ModuleUploadDiagnosticsRecorder.RecordFailure(
                message,
                uploadTargets,
                _diagnosticsStore,
                _cloudDiagnosticsStore,
                new ModuleUploadDiagnosticsRoute(
                    CodeOptions.Mes.Channels.Outbound,
                    DependencyInjection.ModuleKey,
                    "plc_outbound_blocked",
                    "plc_outbound_exception"),
                diagnosticsIdentity);
            Interaction.ReplyException(trigger);
            return;
        }

        if (!enqueueResult.IsDurablyAccepted)
        {
            var failure = FormatRejectedResult(enqueueResult);
            RecordOutbound(cellData, failure);
            ModuleUploadDiagnosticsRecorder.RecordFailure(
                failure,
                uploadTargets,
                _diagnosticsStore,
                _cloudDiagnosticsStore,
                new ModuleUploadDiagnosticsRoute(
                    CodeOptions.Mes.Channels.Outbound,
                    DependencyInjection.ModuleKey,
                    "plc_outbound_blocked",
                    "plc_outbound_enqueue_failed"),
                diagnosticsIdentity);
            Interaction.ReplyException(trigger);
            return;
        }

        var result = enqueueResult.WasOverflow
            ? HomogenizationText.Get(
                "Homogenization_Outbound_OverflowReceived",
                "出料已接收，数据已进入溢出持久化。")
            : HomogenizationText.Get("Homogenization_Outbound_Received", "出料已接收。");

        ModuleContext.MarkProcessedTray(
            HomogenizationTrayCodeStage.Outbound,
            cellData.TrayCode,
            "出站已接收",
            cellData.CompletedTime ?? ProductionTime.BusinessNow);
        RecordOutbound(cellData, result);
        Interaction.ReplyOk(trigger);
    }

    private HomogenizationCellData BuildRecord(DataPipelineUploadTargets uploadTargets)
    {
        var outbound = Codec.CaptureOutboundReadings();
        return new HomogenizationCellData
        {
            RecordKind = HomogenizationCellData.RecordKindOutbound,
            TrayCode = Codec.ReadTrayCode(),
            DeviceName = ModuleContext.DeviceName,
            DeviceCode = _deviceService.CurrentDevice?.ClientCode ?? ModuleContext.DeviceName,
            PlcDeviceId = ModuleContext.NetworkDeviceId,
            UploadTargets = uploadTargets,
            InboundTime = ModuleContext.LastInboundAt,
            CompletedTime = ProductionTime.BusinessNow,
            RuntimeStatus = HomogenizationText.Get("Homogenization_Outbound_PendingUpload", "出料待上传"),
            RealtimeSnapshot = Codec.CaptureRealtimeSnapshot(),
            RecipeSnapshot = ModuleContext.LastRecipeSnapshot,
            EquipmentStatusSnapshot = ModuleContext.LastEquipmentStatusSnapshot
                ?? Codec.CaptureEquipmentStatusSnapshot(CodeOptions.Mes),
            CntActualKg = outbound.CntActualKg,
            CntTargetKg = outbound.CntTargetKg,
            CntTankAWeightKg = outbound.CntTankAWeightKg,
            CntTankBWeightKg = outbound.CntTankBWeightKg,
            NmpActualKg = outbound.NmpActualKg,
            NmpTargetKg = outbound.NmpTargetKg,
            GlueActualKg = outbound.GlueActualKg,
            SetStirringTimeMinutes = outbound.SetStirringTimeMinutes,
            RemainingStirringTimeMinutes = outbound.RemainingStirringTimeMinutes,
            SetDispersionTimeMinutes = outbound.SetDispersionTimeMinutes,
            RemainingDispersionTimeMinutes = outbound.RemainingDispersionTimeMinutes
        };
    }

    private void RecordOutbound(HomogenizationCellData cellData, string result)
    {
        ModuleContext.LastOutboundTrayCode = cellData.TrayCode;
        ModuleContext.LastOutboundAt = cellData.CompletedTime ?? ProductionTime.BusinessNow;
        ModuleContext.LastOutboundResult = result;
        ModuleContext.RecordOutbound(cellData);
    }

    private void RecordOutboundResult(string result)
    {
        ModuleContext.LastOutboundAt = ProductionTime.BusinessNow;
        ModuleContext.LastOutboundResult = result;
    }

    private static string FormatRejectedResult(DataPipelineEnqueueResult enqueueResult)
    {
        var reason = string.IsNullOrWhiteSpace(enqueueResult.ReasonCode)
            ? "unknown"
            : enqueueResult.ReasonCode;

        return enqueueResult.WasOverflow
            ? $"出料未接收，溢出持久化未写入任何补偿目标（{reason}）。"
            : $"出料未接收，数据管道拒绝入队（{reason}）。";
    }
}
