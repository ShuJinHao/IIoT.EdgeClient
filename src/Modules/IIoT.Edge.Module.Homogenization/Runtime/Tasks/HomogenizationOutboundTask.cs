using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
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

    public HomogenizationOutboundTask(
        IPlcBuffer buffer,
        ILogicalSignalAccessor<HomogenizationSignal> signals,
        HomogenizationContext context,
        IDeviceService deviceService,
        IDataPipelineService dataPipelineService,
        HomogenizationCellDataValidator validator,
        IMesUploadDiagnosticsStore diagnosticsStore,
        IModuleParamProvider<MesParam, CloudParam, BusinessParam> parameters,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, signals, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _deviceService = deviceService;
        _dataPipelineService = dataPipelineService;
        _validator = validator;
        _diagnosticsStore = diagnosticsStore;
        _parameters = parameters;
    }

    /// <summary>
    /// 出料握手任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Outbound";

    protected override async Task DoCoreAsync()
    {
        switch (Step)
        {
            case 0:
                if (Signals.ReadUInt16(HomogenizationSignal.出料触发) == CodeOptions.Plc.SignalTrigger)
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
                    Signals.WriteUInt16(HomogenizationSignal.出料应答, CodeOptions.Plc.AckException);
                    Logger.Error($"[{ModuleContext.DeviceName}] {TaskName} {message}");
                    Step = 30;
                }

                break;

            case 30:
                if (Signals.ReadUInt16(HomogenizationSignal.出料触发) == CodeOptions.Plc.SignalReset)
                {
                    Signals.WriteUInt16(HomogenizationSignal.出料应答, CodeOptions.Plc.SignalReset);
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
            Signals.WriteUInt16(HomogenizationSignal.出料应答, CodeOptions.Plc.AckException);
            return;
        }

        var parameters = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        if (parameters.Business<bool>(BusinessParam.启用托盘码重码验证)
            && ModuleContext.HasProcessedTray(HomogenizationTrayCodeStage.Outbound, cellData.TrayCode))
        {
            var message = FormatDuplicateMessage(HomogenizationTrayCodeStage.Outbound, cellData.TrayCode);
            Signals.WriteUInt16(HomogenizationSignal.出料应答, CodeOptions.Plc.AckMesNg);
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
            Signals.WriteUInt16(HomogenizationSignal.出料应答, CodeOptions.Plc.AckException);
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
        Signals.WriteUInt16(HomogenizationSignal.出料应答, CodeOptions.Plc.AckOk);
    }

    private HomogenizationCellData BuildRecord()
    {
        var trayCode = Signals.ReadAscii(HomogenizationSignal.托盘码);
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
            CntActualKg = Signals.ReadUInt16(HomogenizationSignal.出料CNT实际值),
            CntTargetKg = Signals.ReadUInt16(HomogenizationSignal.出料CNT目标值),
            CntTankAWeightKg = Signals.ReadUInt16(HomogenizationSignal.出料CNTA罐重量),
            CntTankBWeightKg = Signals.ReadUInt16(HomogenizationSignal.出料CNTB罐重量),
            NmpActualKg = Signals.ReadUInt16(HomogenizationSignal.出料NMP实际值),
            NmpTargetKg = Signals.ReadUInt16(HomogenizationSignal.出料NMP目标值),
            GlueActualKg = Signals.ReadUInt16(HomogenizationSignal.出料胶液实际值),
            SetStirringTimeMinutes = Signals.ReadUInt16(HomogenizationSignal.出料设定搅拌时间),
            RemainingStirringTimeMinutes = Signals.ReadUInt16(HomogenizationSignal.出料剩余搅拌时间),
            SetDispersionTimeMinutes = Signals.ReadUInt16(HomogenizationSignal.出料设定分散时间),
            RemainingDispersionTimeMinutes = Signals.ReadUInt16(HomogenizationSignal.出料剩余分散时间)
        };
    }

    private static string FormatDuplicateMessage(HomogenizationTrayCodeStage stage, string trayCode)
    {
        var stageName = stage == HomogenizationTrayCodeStage.Inbound ? "进站" : "出站";
        return $"托盘码重复，已按业务 NG 拒绝{stageName}：{trayCode.Trim()}。";
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
