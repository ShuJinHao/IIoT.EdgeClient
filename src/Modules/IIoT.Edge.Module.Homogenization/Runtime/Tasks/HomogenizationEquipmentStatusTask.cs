using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
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
/// 设备状态握手任务：PLC 触发后读取状态码并上传 MES。
/// </summary>
internal sealed class HomogenizationEquipmentStatusTask : HomogenizationTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly HomogenizationMesScenarioChannel _mesChannel;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;

    public HomogenizationEquipmentStatusTask(
        IPlcBuffer buffer,
        ILogicalSignalAccessor<HomogenizationSignal> signals,
        HomogenizationContext context,
        IDeviceService deviceService,
        HomogenizationMesScenarioChannel mesChannel,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, signals, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _deviceService = deviceService;
        _mesChannel = mesChannel;
        _diagnosticsStore = diagnosticsStore;
    }

    /// <summary>
    /// 设备状态上传任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.EquipmentStatus";

    protected override async Task DoCoreAsync()
    {
        switch (Step)
        {
            case 0:
                if (Signals.ReadUInt16(HomogenizationSignal.设备状态上传触发) == CodeOptions.Plc.SignalTrigger)
                {
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 设备状态上传触发。");
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
                    var message = $"设备状态上传处理异常：{ex.Message}";
                    _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.EquipmentStatus, message);
                    ModuleContext.LastEquipmentStatusAt = ProductionTime.BusinessNow;
                    ModuleContext.LastEquipmentStatusResult = message;
                    Signals.WriteUInt16(HomogenizationSignal.设备状态应答, CodeOptions.Plc.AckException);
                    Logger.Error($"[{ModuleContext.DeviceName}] {TaskName} {message}");
                    Step = 30;
                }

                break;

            case 30:
                if (Signals.ReadUInt16(HomogenizationSignal.设备状态上传触发) == CodeOptions.Plc.SignalReset)
                {
                    Signals.WriteUInt16(HomogenizationSignal.设备状态应答, CodeOptions.Plc.SignalReset);
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 设备状态上传复位。");
                    Step = 0;
                }

                break;
        }
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        var snapshot = Codec.CaptureEquipmentStatusSnapshot(CodeOptions.Mes);
        var result = await _mesChannel
            .UploadEquipmentStatusAsync(_deviceService.CurrentDevice, snapshot, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(CodeOptions.Mes.Channels.EquipmentStatus);
        }
        else
        {
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.EquipmentStatus, result.Message);
        }

        ModuleContext.LastEquipmentStatusAt = snapshot.CapturedAt;
        ModuleContext.LastEquipmentStatusResult = result.Message;
        ModuleContext.LastEquipmentStatusSnapshot = snapshot;

        Signals.WriteUInt16(HomogenizationSignal.设备状态应答, ResolveAck(result));
    }
}
