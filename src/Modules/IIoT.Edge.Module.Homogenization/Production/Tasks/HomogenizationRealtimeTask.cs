using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.Module.Sdk.DataPipeline;
using IIoT.Edge.Module.Sdk.Diagnostics;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Production.Tasks;

/// <summary>
/// 实时上传任务：周期采集 PLC 单点实时数据快照并按配置进入上传链路。
/// </summary>
internal sealed class HomogenizationRealtimeTask : PeriodicSnapshotUploadTaskBase<HomogenizationRealtimeSnapshot>
{
    private readonly HomogenizationContext _context;
    private readonly IDeviceService _deviceService;
    private readonly IDataPipelineService _dataPipelineService;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly ICloudUploadDiagnosticsStore _cloudDiagnosticsStore;
    private readonly ICloudExecutionPolicy _cloudExecutionPolicy;
    private readonly IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> _parameters;
    private readonly IHomogenizationProductionGate _productionGate;
    private readonly HomogenizationCodeOptions _codeOptions;
    private readonly int _taskLoopInterval;
    private readonly HomogenizationSignalCodec _codec;
    private DataPipelineUploadTargets _lastUploadTargets;

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
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
        ICloudExecutionPolicy cloudExecutionPolicy,
        IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> parameters,
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
        _cloudDiagnosticsStore = cloudDiagnosticsStore;
        _cloudExecutionPolicy = cloudExecutionPolicy;
        _parameters = parameters;
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
        var parameterSnapshot = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        var mesEnabled = parameterSnapshot.Mes<bool>(HomogenizationParams.Mes.启用);
        var cloudEnabled = _cloudExecutionPolicy.IsEnabled;
        var uploadTargets = DataPipelineUploadTargetPolicy.Resolve(mesEnabled, cloudEnabled);
        _lastUploadTargets = uploadTargets;

        if (uploadTargets == DataPipelineUploadTargets.None)
        {
            return MesCallResult.Disabled("MES/Cloud 上传已关闭，实时数据上传已跳过。");
        }

        if (mesEnabled)
        {
            var gateResult = await _productionGate.EnsureReadyAsync(_context, cancellationToken).ConfigureAwait(false);
            if (!gateResult.IsSuccess)
            {
                return gateResult;
            }
        }

        var realtimeFingerprint = snapshot.CreateFingerprint();
        if (string.Equals(
                _context.LastRealtimeFingerprint,
                realtimeFingerprint,
                StringComparison.Ordinal))
        {
            return MesCallResult.Success("匀浆实时数据未变化，已跳过实时上传。");
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
            UploadTargets = uploadTargets
        };

        try
        {
            var enqueueResult = await _dataPipelineService
                .EnqueueAsync(CreateCompletedRecord(cellData, includeMesPlanContext: mesEnabled), cancellationToken)
                .ConfigureAwait(false);

            var result = ModuleDataPipelineEnqueueResultMapper.ToQueuedUploadResult(
                enqueueResult,
                "实时数据",
                uploadTargets);
            if (enqueueResult.IsDurablyAccepted)
            {
                _context.LastRealtimeFingerprint = realtimeFingerprint;
            }

            return result;
        }
        catch (Exception ex)
        {
            return MesCallResult.TransportFailure($"实时数据处理异常：{ex.Message}");
        }
    }

    protected override Task OnSnapshotUploadedAsync(
        HomogenizationRealtimeSnapshot snapshot,
        MesCallResult result,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            ModuleUploadDiagnosticsRecorder.RecordResult(
                result,
                _lastUploadTargets,
                _diagnosticsStore,
                _cloudDiagnosticsStore,
                new ModuleUploadDiagnosticsRoute(
                    _codeOptions.Mes.Channels.Realtime,
                    DependencyInjection.ModuleKey,
                    "plc_realtime_blocked",
                    "plc_realtime_enqueue_failed"),
                new ModuleUploadDiagnosticsIdentity(
                    _context.DeviceName,
                    DependencyInjection.ModuleKey,
                    TaskName,
                    "实时数据上传"));
        }

        _context.LastRealtimeAt = snapshot.CapturedAt;
        _context.LastRealtimeResult = result.Message;
        _context.LastRealtimeSnapshot = snapshot;
        return Task.CompletedTask;
    }

    private CellCompletedRecord CreateCompletedRecord(
        HomogenizationCellData cellData,
        bool includeMesPlanContext)
        => new()
        {
            CellData = cellData,
            NetworkDeviceId = _context.NetworkDeviceId,
            DeviceName = _context.DeviceName,
            ModuleId = DependencyInjection.ModuleKey,
            TaskKey = TaskName,
            PlanSessionId = includeMesPlanContext ? _context.PlanSessionId ?? string.Empty : string.Empty,
            MainPlanCode = includeMesPlanContext ? _context.SelectedProductionPlan?.MainPlanCode ?? string.Empty : string.Empty,
            TraceBatchNumber = includeMesPlanContext ? _context.TraceBatchNumber ?? string.Empty : string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };

}
