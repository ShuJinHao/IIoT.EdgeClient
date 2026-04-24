using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Runtime;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

internal sealed class HomogenizationInboundTask : HomogenizationTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly IHomogenizationMesApiService _mesApiService;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;

    public HomogenizationInboundTask(
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

    public override string TaskName => "Homogenization.Inbound";

    private static string TriggerLabel => HomogenizationPlcSignalProfile.InboundTrigger.Label;

    private static string AckLabel => HomogenizationPlcSignalProfile.InboundAck.Label;

    protected override async Task DoCoreAsync()
    {
        switch (Step)
        {
            case 0:
                if (Codec.ReadWord(TriggerLabel) == CodeOptions.Plc.SignalTrigger)
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
                    Codec.WriteWord(AckLabel, CodeOptions.Plc.AckException);
                    Logger.Error($"[{ModuleContext.DeviceName}] {TaskName} {message}");
                    Step = 30;
                }

                break;

            case 30:
                if (Codec.ReadWord(TriggerLabel) == CodeOptions.Plc.SignalReset)
                {
                    Codec.WriteWord(AckLabel, CodeOptions.Plc.SignalReset);
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} 进站复位。");
                    Step = 0;
                }

                break;
        }
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        var trayCode = Codec.ReadAsciiString(HomogenizationPlcSignalProfile.TrayCode.Label);
        if (string.IsNullOrWhiteSpace(trayCode))
        {
            const string message = "托盘码不能为空。";
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Inbound, message);
            RecordInboundResult(string.Empty, message);
            Codec.WriteWord(AckLabel, CodeOptions.Plc.AckException);
            return;
        }

        var result = await _mesApiService
            .UploadInboundAsync(_deviceService.CurrentDevice, trayCode, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(CodeOptions.Mes.Channels.Inbound);
        }
        else
        {
            _diagnosticsStore.RecordFailure(CodeOptions.Mes.Channels.Inbound, result.Message);
        }

        RecordInboundResult(trayCode, result.Message);
        Codec.WriteWord(AckLabel, ResolveAck(result));
    }

    private void RecordInboundResult(string trayCode, string result)
    {
        ModuleContext.LastInboundTrayCode = trayCode;
        ModuleContext.LastInboundAt = DateTime.UtcNow;
        ModuleContext.LastInboundResult = result;
    }
}
