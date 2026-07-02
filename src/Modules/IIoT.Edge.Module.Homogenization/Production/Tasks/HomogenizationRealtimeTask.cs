using IIoT.Edge.Application.Abstractions.Device;
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
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Module.Homogenization.Production.Tasks;

/// <summary>
/// 实时上传任务：周期采集 PLC 单点实时数据快照并上传 MES。
/// </summary>
internal sealed class HomogenizationRealtimeTask : PeriodicSnapshotUploadTaskBase<HomogenizationRealtimeSnapshot>
{
    private readonly HomogenizationContext _context;
    private readonly IDeviceService _deviceService;
    private readonly IDataPipelineService _dataPipelineService;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly IHomogenizationProductionGate _productionGate;
    private readonly HomogenizationCodeOptions _codeOptions;
    private readonly int _taskLoopInterval;
    private readonly HomogenizationSignalCodec _codec;

    /// <summary>
    /// 创建匀浆实时数据上传任务。
    /// </summary>
    public HomogenizationRealtimeTask(
        IPlcBuffer buffer,
        HomogenizationSignalCodec codec,
        HomogenizationContext context,
        IDeviceService deviceService,
        IDataPipelineService dataPipelineService,
        IMesUploadDiagnosticsStore diagnosticsStore,
        IHomogenizationProductionGate productionGate,
        ILogService logger,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, context, logger)
    {
        _context = context;
        _deviceService = deviceService;
        _dataPipelineService = dataPipelineService;
        _diagnosticsStore = diagnosticsStore;
        _productionGate = productionGate;
        _codeOptions = codeOptions.Value;
        _codec = codec;
        var runtime = moduleOptions.Value.Runtime;
        _taskLoopInterval = Math.Max(runtime.MinRealtimeLoopIntervalMs, runtime.RealtimeLoopIntervalMs);
    }

    /// <summary>
    /// 实时上传任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Realtime";

    /// <summary>
    /// 实时快照采集和上传循环间隔，按配置最小值保护。
    /// </summary>
    protected override int TaskLoopInterval => _taskLoopInterval;

    protected override HomogenizationRealtimeSnapshot CaptureSnapshot()
        => _codec.CaptureRealtimeSnapshot();

    protected override Task<MesCallResult> UploadSnapshotAsync(
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken)
        => UploadSnapshotWithGateAsync(snapshot, cancellationToken);

    private async Task<MesCallResult> UploadSnapshotWithGateAsync(
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var gateResult = await _productionGate.EnsureReadyAsync(_context, cancellationToken).ConfigureAwait(false);
        if (!gateResult.IsSuccess)
        {
            return gateResult;
        }

        var cellData = new HomogenizationCellData
        {
            RecordKind = HomogenizationCellData.RecordKindRealtime,
            DeviceName = _context.DeviceName,
            DeviceCode = _deviceService.CurrentDevice?.ClientCode ?? _context.DeviceName,
            PlcDeviceId = _context.NetworkDeviceId,
            CompletedTime = snapshot.CapturedAt,
            RuntimeStatus = "实时数据待上传",
            RealtimeSnapshot = snapshot,
            UploadTargets = DataPipelineUploadTargets.Mes
        };

        var enqueueResult = await _dataPipelineService
            .EnqueueAsync(CreateCompletedRecord(cellData), cancellationToken)
            .ConfigureAwait(false);

        return ToQueueResult(enqueueResult);
    }

    protected override Task OnSnapshotUploadedAsync(
        HomogenizationRealtimeSnapshot snapshot,
        MesCallResult result,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            _diagnosticsStore.RecordFailure(_codeOptions.Mes.Channels.Realtime, result.Message);
        }

        _context.LastRealtimeAt = snapshot.CapturedAt;
        _context.LastRealtimeResult = result.Message;
        _context.LastRealtimeSnapshot = snapshot;
        return Task.CompletedTask;
    }

    private static MesCallResult ToQueueResult(DataPipelineEnqueueResult enqueueResult)
    {
        if (enqueueResult.IsDurablyAccepted)
        {
            return MesCallResult.Success(enqueueResult.WasOverflow
                ? "实时数据已接收，数据已进入溢出持久化。"
                : "实时数据已进入 MES 上传队列。");
        }

        var reason = string.IsNullOrWhiteSpace(enqueueResult.ReasonCode)
            ? "unknown"
            : enqueueResult.ReasonCode;
        return MesCallResult.TransportFailure($"实时数据未接收，数据管道拒绝入队（{reason}）。");
    }

    private CellCompletedRecord CreateCompletedRecord(HomogenizationCellData cellData)
        => new()
        {
            CellData = cellData,
            NetworkDeviceId = _context.NetworkDeviceId,
            DeviceName = _context.DeviceName,
            ModuleId = DependencyInjection.ModuleKey,
            TaskKey = TaskName,
            PlanSessionId = _context.PlanSessionId ?? string.Empty,
            MainPlanCode = _context.SelectedProductionPlan?.MainPlanCode ?? string.Empty,
            TraceBatchNumber = _context.TraceBatchNumber ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };
}
