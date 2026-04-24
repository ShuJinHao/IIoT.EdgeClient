using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Runtime;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

internal sealed class HomogenizationRecipeTask : HomogenizationTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly IHomogenizationMesApiService _mesApiService;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;

    public HomogenizationRecipeTask(
        IPlcBuffer buffer,
        HomogenizationContext context,
        IDeviceService deviceService,
        IHomogenizationMesApiService mesApiService,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger,
        HomogenizationModuleOptions moduleOptions,
        HomogenizationCodeOptions codeOptions)
        : base(buffer, context, logger, codeOptions, moduleOptions)
    {
        _deviceService = deviceService;
        _mesApiService = mesApiService;
        _diagnosticsStore = diagnosticsStore;
    }

    public override string TaskName => "Homogenization.Recipe";

    private static string TriggerLabel => HomogenizationPlcSignalProfile.RecipeTrigger.Label;

    private static string AckLabel => HomogenizationPlcSignalProfile.RecipeAck.Label;

    protected override async Task DoCoreAsync()
    {
        switch (Step)
        {
            case 0:
                if (Codec.ReadWord(TriggerLabel) == CodeOptions.Plc.SignalTrigger)
                {
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 配方上传触发。");
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
                    ModuleContext.LastRecipeAt = DateTime.UtcNow;
                    ModuleContext.LastRecipeResult = message;
                    Codec.WriteWord(AckLabel, CodeOptions.Plc.AckException);
                    Logger.Error($"[{ModuleContext.DeviceName}] {TaskName} {message}");
                    Step = 30;
                }

                break;

            case 30:
                if (Codec.ReadWord(TriggerLabel) == CodeOptions.Plc.SignalReset)
                {
                    Codec.WriteWord(AckLabel, CodeOptions.Plc.SignalReset);
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 配方上传复位。");
                    Step = 0;
                }

                break;
        }
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        var snapshot = Codec.CaptureRecipeSnapshot();
        var result = await _mesApiService
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

        Codec.WriteWord(AckLabel, ResolveAck(result));
    }
}
