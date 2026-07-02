using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Module.Homogenization.Production.Tasks;

/// <summary>
/// 设备状态握手任务：PLC 触发后读取状态码并上传 MES。
/// </summary>
internal sealed class HomogenizationEquipmentStatusTask : HomogenizationTaskBase
{
    private readonly IDataPipelineService _dataPipelineService;
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
        IDataPipelineService dataPipelineService,
        IMesUploadDiagnosticsStore diagnosticsStore,
        IHomogenizationProductionGate productionGate,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, interaction, codec, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _dataPipelineService = dataPipelineService;
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
            EnqueueEquipmentStatusAsync,
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

    private async Task<MesCallResult> EnqueueEquipmentStatusAsync(
        HomogenizationEquipmentStatusSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var cellData = new HomogenizationCellData
        {
            RecordKind = HomogenizationCellData.RecordKindEquipmentStatus,
            DeviceName = ModuleContext.DeviceName,
            DeviceCode = ModuleContext.DeviceName,
            PlcDeviceId = ModuleContext.NetworkDeviceId,
            CompletedTime = snapshot.CapturedAt,
            RuntimeStatus = "设备状态待上传",
            EquipmentStatusSnapshot = snapshot,
            UploadTargets = DataPipelineUploadTargets.Mes
        };

        var enqueueResult = await _dataPipelineService
            .EnqueueAsync(CreatePipelineRecord(cellData), cancellationToken)
            .ConfigureAwait(false);

        return ToMesQueueResult(
            enqueueResult,
            "设备状态已进入 MES 上传队列。",
            "设备状态已接收，数据已进入溢出持久化。",
            "设备状态未接收，数据管道拒绝入队");
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
