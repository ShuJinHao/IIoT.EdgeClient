using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
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
/// 配方握手任务：PLC 触发后按配方连续读数据区读取数组并上传 MES。
/// </summary>
internal sealed class HomogenizationRecipeTask : HomogenizationTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly HomogenizationMesScenarioChannel _mesChannel;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;

    /// <summary>
    /// 创建匀浆配方上传握手任务。
    /// </summary>
    public HomogenizationRecipeTask(
        IPlcBuffer buffer,
        HomogenizationPlcHandshakeAccessor interaction,
        HomogenizationSignalCodec codec,
        HomogenizationContext context,
        IDeviceService deviceService,
        HomogenizationMesScenarioChannel mesChannel,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, interaction, codec, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _deviceService = deviceService;
        _mesChannel = mesChannel;
        _diagnosticsStore = diagnosticsStore;
    }

    /// <summary>
    /// 配方上传任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Recipe";

    protected override async Task DoCoreAsync()
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.工艺参数上传;

        switch (Step)
        {
            case 0:
                if (Interaction.IsTriggered(trigger))
                {
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 工艺参数上传已触发。");
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
                    var message = $"配方上传处理异常：{ex.Message}";
                    _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Recipe, message);
                    ModuleContext.LastRecipeAt = ProductionTime.BusinessNow;
                    ModuleContext.LastRecipeResult = message;
                    Interaction.ReplyException(trigger);
                    Logger.Error($"[{ModuleContext.DeviceName}] {TaskName} {message}");
                    Step = 30;
                }

                break;

            case 30:
                if (Interaction.IsReset(trigger))
                {
                    Interaction.ReplyReset(trigger);
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 配方上传复位。");
                    Step = 0;
                }

                break;
        }
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.工艺参数上传;
        var snapshot = Codec.CaptureRecipeSnapshot();
        var result = await _mesChannel
            .UploadRecipeAsync(_deviceService.CurrentDevice, snapshot, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(CodeOptions.Mes.Channels.Recipe);
        }
        else
        {
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Recipe, result.Message);
        }

        ModuleContext.LastRecipeAt = snapshot.CapturedAt;
        ModuleContext.LastRecipeResult = result.Message;
        ModuleContext.LastRecipeSnapshot = snapshot;

        Interaction.ReplyResult(trigger, result);
    }
}

