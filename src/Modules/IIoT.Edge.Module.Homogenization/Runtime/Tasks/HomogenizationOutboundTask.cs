using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Resources;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.SharedKernel.DataPipeline;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

/// <summary>
/// 出料握手任务：PLC 触发后读取出料数据并写入本地数据管道。
/// </summary>
internal sealed class HomogenizationOutboundTask : HomogenizationTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly IDataPipelineService _dataPipelineService;
    private readonly HomogenizationCellDataValidator _validator;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly IModuleParamProvider<MesParam, CloudParam, BusinessParam> _parameters;
    private readonly HomogenizationTrayCodeGuard _trayCodeGuard;

    public HomogenizationOutboundTask(
        IPlcBuffer buffer,
        HomogenizationContext context,
        IDeviceService deviceService,
        IDataPipelineService dataPipelineService,
        HomogenizationCellDataValidator validator,
        IMesUploadDiagnosticsStore diagnosticsStore,
        IModuleParamProvider<MesParam, CloudParam, BusinessParam> parameters,
        HomogenizationTrayCodeGuard trayCodeGuard,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _deviceService = deviceService;
        _dataPipelineService = dataPipelineService;
        _validator = validator;
        _diagnosticsStore = diagnosticsStore;
        _parameters = parameters;
        _trayCodeGuard = trayCodeGuard;
    }

    /// <summary>
    /// 出料握手任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Outbound";

    private static string TriggerLabel => HomogenizationPlcSignalProfile.OutboundTrigger.Label;

    private static string AckLabel => HomogenizationPlcSignalProfile.OutboundAck.Label;

    protected override async Task DoCoreAsync()
    {
        switch (Step)
        {
            case 0:
                if (Codec.ReadWord(TriggerLabel) == CodeOptions.Plc.SignalTrigger)
                {
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 出料触发。");
                    Step = 10;
                }

                break;

            case 10:
                try
                {
                    await ProcessTriggerAsync(TaskCancellationToken).ConfigureAwait(false);
                    Step = 30;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var message = $"出料处理异常：{ex.Message}";
                    ModuleContext.LastOutboundAt = ProductionTime.BusinessNow;
                    ModuleContext.LastOutboundResult = message;
                    _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Outbound, message);
                    Codec.WriteWord(AckLabel, CodeOptions.Plc.AckException);
                    Logger.Error($"[{ModuleContext.DeviceName}] {TaskName} {message}");
                    Step = 30;
                }

                break;

            case 30:
                if (Codec.ReadWord(TriggerLabel) == CodeOptions.Plc.SignalReset)
                {
                    Codec.WriteWord(AckLabel, CodeOptions.Plc.SignalReset);
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 出料复位。");
                    Step = 0;
                }

                break;
        }
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        var cellData = BuildRecord();
        if (!_validator.TryValidate(cellData, out var error))
        {
            var message = error ?? "出料校验失败。";
            RecordOutbound(cellData, message);
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Outbound, message);
            Codec.WriteWord(AckLabel, CodeOptions.Plc.AckException);
            return;
        }

        var parameters = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        if (_trayCodeGuard.IsDuplicateEnabled(parameters)
            && _trayCodeGuard.IsDuplicate(ModuleContext, HomogenizationTrayCodeStage.Outbound, cellData.TrayCode))
        {
            var message = _trayCodeGuard.FormatDuplicateMessage(HomogenizationTrayCodeStage.Outbound, cellData.TrayCode);
            Codec.WriteWord(AckLabel, CodeOptions.Plc.AckMesNg);
            RecordOutbound(cellData, message);
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Outbound, message);
            return;
        }

        var enqueueResult = await _dataPipelineService
            .EnqueueAsync(new CellCompletedRecord { CellData = cellData }, cancellationToken)
            .ConfigureAwait(false);

        if (!enqueueResult.IsDurablyAccepted)
        {
            var failure = FormatRejectedResult(enqueueResult);
            RecordOutbound(cellData, failure);
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Outbound, failure);
            Codec.WriteWord(AckLabel, CodeOptions.Plc.AckException);
            return;
        }

        var result = enqueueResult.WasOverflow
            ? HomogenizationText.Get(
                "Homogenization_Outbound_OverflowReceived",
                "出料已接收，数据已进入溢出持久化。")
            : HomogenizationText.Get("Homogenization_Outbound_Received", "出料已接收。");

        _trayCodeGuard.MarkProcessed(
            ModuleContext,
            HomogenizationTrayCodeStage.Outbound,
            cellData.TrayCode,
            "出站已接收",
            cellData.CompletedTime ?? ProductionTime.BusinessNow);
        RecordOutbound(cellData, result);
        Codec.WriteWord(AckLabel, CodeOptions.Plc.AckOk);
    }

    private HomogenizationCellData BuildRecord()
    {
        var trayCode = Codec.ReadAsciiString(HomogenizationPlcSignalProfile.TrayCode.Label);
        return new HomogenizationCellData
        {
            TrayCode = trayCode,
            DeviceName = ModuleContext.DeviceName,
            DeviceCode = _deviceService.CurrentDevice?.ClientCode ?? ModuleContext.DeviceName,
            InboundTime = ModuleContext.LastInboundAt,
            CompletedTime = ProductionTime.BusinessNow,
            RuntimeStatus = HomogenizationText.Get("Homogenization_Outbound_PendingUpload", "出料待上传"),
            RealtimeSnapshot = Codec.CaptureRealtimeSnapshot(),
            RecipeSnapshot = ModuleContext.LastRecipeSnapshot,
            EquipmentStatusSnapshot = ModuleContext.LastEquipmentStatusSnapshot
                ?? Codec.CaptureEquipmentStatusSnapshot(CodeOptions.Mes),
            CntActualKg = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundCntActual.Label),
            CntTargetKg = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundCntTarget.Label),
            CntTankAWeightKg = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundCntTankAWeight.Label),
            CntTankBWeightKg = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundCntTankBWeight.Label),
            NmpActualKg = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundNmpActual.Label),
            NmpTargetKg = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundNmpTarget.Label),
            GlueActualKg = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundGlueActual.Label),
            SetStirringTimeMinutes = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundSetStirringTime.Label),
            RemainingStirringTimeMinutes = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundRemainingStirringTime.Label),
            SetDispersionTimeMinutes = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundSetDispersionTime.Label),
            RemainingDispersionTimeMinutes = Codec.ReadWord(HomogenizationPlcSignalProfile.OutboundRemainingDispersionTime.Label)
        };
    }

    private void RecordOutbound(HomogenizationCellData cellData, string result)
    {
        ModuleContext.LastOutboundTrayCode = cellData.TrayCode;
        ModuleContext.LastOutboundAt = cellData.CompletedTime ?? ProductionTime.BusinessNow;
        ModuleContext.LastOutboundResult = result;
        ModuleContext.RecordOutbound(cellData);
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
