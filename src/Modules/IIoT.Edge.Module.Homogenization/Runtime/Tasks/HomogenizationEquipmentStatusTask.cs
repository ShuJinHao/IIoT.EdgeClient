using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

/// <summary>
/// 设备状态握手任务：PLC 触发后读取状态码并上传 MES。
/// </summary>
internal sealed class HomogenizationEquipmentStatusTask : HomogenizationTaskBase
{
    private readonly IDeviceService _deviceService;
    private readonly IHomogenizationMesScenarioChannel _mesChannel;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly IHomogenizationProductionGate _productionGate;

    /// <summary>
    /// 创建匀浆设备状态上传握手任务。
    /// </summary>
    public HomogenizationEquipmentStatusTask(
        IPlcBuffer buffer,
        HomogenizationPlcHandshakeAccessor interaction,
        HomogenizationSignalCodec codec,
        HomogenizationContext context,
        IDeviceService deviceService,
        IHomogenizationMesScenarioChannel mesChannel,
        IMesUploadDiagnosticsStore diagnosticsStore,
        IHomogenizationProductionGate productionGate,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, interaction, codec, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _deviceService = deviceService;
        _mesChannel = mesChannel;
        _diagnosticsStore = diagnosticsStore;
        _productionGate = productionGate;
    }

    /// <summary>
    /// 设备状态上传任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.EquipmentStatus";

    protected override async Task DoCoreAsync()
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.设备状态上传;

        await ExecuteMesSnapshotHandshakeAsync(
            trigger,
            "设备状态上传触发。",
            "设备状态上传复位。",
            "设备状态上传处理异常",
            CodeOptions.Mes.Channels.EquipmentStatus,
            _productionGate,
            _diagnosticsStore,
            () => Codec.CaptureEquipmentStatusSnapshot(CodeOptions.Mes),
            (snapshot, ct) => _mesChannel.UploadEquipmentStatusAsync(_deviceService.CurrentDevice, snapshot, ct),
            message =>
            {
                ModuleContext.LastEquipmentStatusAt = ProductionTime.BusinessNow;
                ModuleContext.LastEquipmentStatusResult = message;
            },
            (snapshot, result) =>
            {
                ModuleContext.LastEquipmentStatusAt = snapshot.CapturedAt;
                ModuleContext.LastEquipmentStatusResult = result.Message;
                ModuleContext.LastEquipmentStatusSnapshot = snapshot;
            },
            WriteCloudDeviceStatusLog).ConfigureAwait(false);
    }

    private void WriteCloudDeviceStatusLog(HomogenizationEquipmentStatusSnapshot snapshot)
    {
        var level = CodeOptions.Cloud.ResolveEquipmentStatusLevel(snapshot);
        var extraMessages = snapshot.Messages.Count == 0
            ? string.Empty
            : $"，消息={string.Join("；", snapshot.Messages)}";
        var message =
            $"设备状态采集：状态码={snapshot.StatusCode}，状态={snapshot.StatusText}，工序={DependencyInjection.ModuleKey}，PLC/设备={ModuleContext.DeviceName}，采集时间={snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss}{extraMessages}。";

        switch (level)
        {
            case "ERROR":
                Logger.Error(message);
                break;
            case "WARN":
                Logger.Warn(message);
                break;
            default:
                Logger.Info(message);
                break;
        }
    }
}
