using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Cloud;
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
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Module.Homogenization.Production.Tasks;

/// <summary>
/// 进站握手任务：PLC 触发后读取托盘码并调用 MES 进站校验接口。
/// </summary>
internal sealed class HomogenizationInboundTask : HomogenizationTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly IDataPipelineService _dataPipelineService;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly ICloudUploadDiagnosticsStore _cloudDiagnosticsStore;
    private readonly IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> _parameters;
    private readonly IHomogenizationProductionGate _productionGate;

    /// <summary>
    /// 创建匀浆进站握手任务。
    /// </summary>
    public HomogenizationInboundTask(
        IPlcBuffer buffer,
        HomogenizationPlcHandshakeAccessor interaction,
        HomogenizationSignalCodec codec,
        HomogenizationContext context,
        IDeviceService deviceService,
        IDataPipelineService dataPipelineService,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
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
        _diagnosticsStore = diagnosticsStore;
        _cloudDiagnosticsStore = cloudDiagnosticsStore;
        _parameters = parameters;
        _productionGate = productionGate;
    }

    /// <summary>
    /// 进站握手任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Inbound";

    protected override async Task DoCoreAsync()
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.扫码进站;

        await ExecuteHandshakeAsync(
            trigger,
            "进站触发。",
            "进站复位。",
            ProcessTriggerAsync,
            static ex => $"进站处理异常：{ex.Message}",
            message =>
            {
                _diagnosticsStore.RecordFailure(
                    CodeOptions.Mes.Channels.Inbound,
                    message,
                    CreateMesDiagnosticsContext("进站上传"));
                RecordInboundResult(string.Empty, message);
            }).ConfigureAwait(false);
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.扫码进站;
        var trayCode = Codec.ReadTrayCode();

        var parameterSnapshot = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        var mesEnabled = parameterSnapshot.Mes<bool>(HomogenizationParams.Mes.启用);
        var cloudEnabled = parameterSnapshot.Cloud<bool>(HomogenizationParams.Cloud.启用);
        var uploadTargets = ResolveUploadTargets(mesEnabled, cloudEnabled);
        if (uploadTargets == DataPipelineUploadTargets.None)
        {
            var disabled = MesCallResult.Disabled("MES/Cloud 上传已关闭，进站上传已跳过。");
            RecordInboundResult(trayCode, disabled.Message);
            Interaction.ReplyResult(trigger, disabled);
            return;
        }

        if (string.IsNullOrWhiteSpace(trayCode))
        {
            var message = HomogenizationText.Get("Homogenization_Error_PalletCodeRequired", "托盘码不能为空。");
            RecordUploadFailureDiagnostics(
                message,
                uploadTargets,
                _diagnosticsStore,
                _cloudDiagnosticsStore,
                CodeOptions.Mes.Channels.Inbound,
                "plc_inbound_invalid_context",
                "进站上传");
            RecordInboundResult(string.Empty, message);
            Interaction.ReplyException(trigger);
            return;
        }

        if (mesEnabled)
        {
            var gateResult = await _productionGate.EnsureReadyAsync(ModuleContext, cancellationToken).ConfigureAwait(false);
            if (!gateResult.IsSuccess)
            {
                RecordUploadBlockedDiagnostics(
                    gateResult.Message,
                    uploadTargets,
                    _diagnosticsStore,
                    _cloudDiagnosticsStore,
                    CodeOptions.Mes.Channels.Inbound,
                    "plc_inbound_blocked",
                    "进站上传");
                RecordInboundResult(trayCode, gateResult.Message);
                Interaction.ReplyResult(trigger, gateResult);
                return;
            }
        }

        var duplicateMessage = await ResolveDuplicateTrayMessageAsync(
            _parameters,
            HomogenizationTrayCodeStage.Inbound,
            trayCode,
            cancellationToken).ConfigureAwait(false);
        if (duplicateMessage is not null)
        {
            Interaction.ReplyMesNg(trigger);
            RecordUploadFailureDiagnostics(
                duplicateMessage,
                uploadTargets,
                _diagnosticsStore,
                _cloudDiagnosticsStore,
                CodeOptions.Mes.Channels.Inbound,
                "plc_inbound_duplicate_tray",
                "进站上传");
            RecordInboundResult(trayCode, duplicateMessage);
            return;
        }

        var cellData = new HomogenizationCellData
        {
            RecordKind = HomogenizationCellData.RecordKindInbound,
            TrayCode = trayCode.Trim(),
            DeviceName = ModuleContext.DeviceName,
            DeviceCode = _deviceService.CurrentDevice?.ClientCode ?? ModuleContext.DeviceName,
            PlcDeviceId = ModuleContext.NetworkDeviceId,
            CompletedTime = ProductionTime.BusinessNow,
            RuntimeStatus = "进站待上传",
            UploadTargets = uploadTargets
        };

        MesCallResult result;
        try
        {
            var enqueueResult = await _dataPipelineService
                .EnqueueAsync(CreatePipelineRecord(cellData, includeMesPlanContext: mesEnabled), cancellationToken)
                .ConfigureAwait(false);
            result = ToUploadQueueResult(enqueueResult, "进站", uploadTargets);
        }
        catch (Exception ex)
        {
            result = MesCallResult.TransportFailure($"进站处理异常：{ex.Message}");
        }

        if (result.IsSuccess)
        {
            ModuleContext.MarkProcessedTray(
                HomogenizationTrayCodeStage.Inbound,
                trayCode,
                "进站已接收",
                ProductionTime.BusinessNow);
        }
        else
        {
            RecordUploadDiagnostics(
                result,
                uploadTargets,
                _diagnosticsStore,
                _cloudDiagnosticsStore,
                CodeOptions.Mes.Channels.Inbound,
                "plc_inbound_enqueue_failed",
                "进站上传");
        }

        RecordInboundResult(trayCode, result.Message);
        Interaction.ReplyResult(trigger, result);
    }

    private void RecordInboundResult(string trayCode, string result)
    {
        ModuleContext.LastInboundTrayCode = trayCode;
        ModuleContext.LastInboundAt = ProductionTime.BusinessNow;
        ModuleContext.LastInboundResult = result;
    }
}
