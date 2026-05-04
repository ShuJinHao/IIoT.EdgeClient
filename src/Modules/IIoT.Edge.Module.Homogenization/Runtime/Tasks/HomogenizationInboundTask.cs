using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Resources;
using IIoT.Edge.Module.Homogenization.Runtime;
using Microsoft.Extensions.Options;
using HomogenizationMesScenarioChannel = IIoT.Edge.Application.Modules.Mes.IMesScenarioChannel<
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationCellData,
    string,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRealtimeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRecipeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationEquipmentStatusSnapshot>;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

/// <summary>
/// 进站握手任务：PLC 触发后读取托盘码并调用 MES 进站校验接口。
/// </summary>
internal sealed class HomogenizationInboundTask : HomogenizationTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly HomogenizationMesScenarioChannel _mesChannel;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly IModuleParamProvider<MesParam, CloudParam, BusinessParam> _parameters;

    public HomogenizationInboundTask(
        IPlcBuffer buffer,
        ILogicalSignalAccessor<HomogenizationSignal> signals,
        HomogenizationContext context,
        IDeviceService deviceService,
        HomogenizationMesScenarioChannel mesChannel,
        IMesUploadDiagnosticsStore diagnosticsStore,
        IModuleParamProvider<MesParam, CloudParam, BusinessParam> parameters,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, signals, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _deviceService = deviceService;
        _mesChannel = mesChannel;
        _diagnosticsStore = diagnosticsStore;
        _parameters = parameters;
    }

    /// <summary>
    /// 进站握手任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Inbound";

    protected override async Task DoCoreAsync()
    {
        switch (Step)
        {
            case 0:
                if (Signals.ReadUInt16(HomogenizationSignal.进站触发) == CodeOptions.Plc.SignalTrigger)
                {
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 进站触发。");
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
                    var message = $"进站处理异常：{ex.Message}";
                    _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Inbound, message);
                    RecordInboundResult(string.Empty, message);
                    Signals.WriteUInt16(HomogenizationSignal.进站应答, CodeOptions.Plc.AckException);
                    Logger.Error($"[{ModuleContext.DeviceName}] {TaskName} {message}");
                    Step = 30;
                }

                break;

            case 30:
                if (Signals.ReadUInt16(HomogenizationSignal.进站触发) == CodeOptions.Plc.SignalReset)
                {
                    Signals.WriteUInt16(HomogenizationSignal.进站应答, CodeOptions.Plc.SignalReset);
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 进站复位。");
                    Step = 0;
                }

                break;
        }
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        var trayCode = Signals.ReadAscii(HomogenizationSignal.托盘码);
        if (string.IsNullOrWhiteSpace(trayCode))
        {
            var message = HomogenizationText.Get("Homogenization_Error_PalletCodeRequired", "托盘码不能为空。");
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Inbound, message);
            RecordInboundResult(string.Empty, message);
            Signals.WriteUInt16(HomogenizationSignal.进站应答, CodeOptions.Plc.AckException);
            return;
        }

        var parameters = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        if (parameters.Business<bool>(BusinessParam.启用托盘码重码验证)
            && ModuleContext.HasProcessedTray(HomogenizationTrayCodeStage.Inbound, trayCode))
        {
            var message = FormatDuplicateMessage(HomogenizationTrayCodeStage.Inbound, trayCode);
            Signals.WriteUInt16(HomogenizationSignal.进站应答, CodeOptions.Plc.AckMesNg);
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Inbound, message);
            RecordInboundResult(trayCode, message);
            return;
        }

        var result = await _mesChannel
            .UploadInboundAsync(_deviceService.CurrentDevice, trayCode, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(CodeOptions.Mes.Channels.Inbound);
            ModuleContext.MarkProcessedTray(
                HomogenizationTrayCodeStage.Inbound,
                trayCode,
                "进站已通过",
                ProductionTime.BusinessNow);
        }
        else
        {
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Inbound, result.Message);
        }

        RecordInboundResult(trayCode, result.Message);
        Signals.WriteUInt16(HomogenizationSignal.进站应答, ResolveAck(result));
    }

    private static string FormatDuplicateMessage(HomogenizationTrayCodeStage stage, string trayCode)
    {
        var stageName = stage == HomogenizationTrayCodeStage.Inbound ? "进站" : "出站";
        return $"托盘码重复，已按业务 NG 拒绝{stageName}：{trayCode.Trim()}。";
    }

    private void RecordInboundResult(string trayCode, string result)
    {
        ModuleContext.LastInboundTrayCode = trayCode;
        ModuleContext.LastInboundAt = ProductionTime.BusinessNow;
        ModuleContext.LastInboundResult = result;
    }
}
