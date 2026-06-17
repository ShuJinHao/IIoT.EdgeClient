using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Mes;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.Module.Sdk.Base;
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
    private readonly IHomogenizationMesScenarioChannel _mesChannel;
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
        IHomogenizationMesScenarioChannel mesChannel,
        IMesUploadDiagnosticsStore diagnosticsStore,
        IHomogenizationProductionGate productionGate,
        ILogService logger,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, context, logger)
    {
        _context = context;
        _deviceService = deviceService;
        _mesChannel = mesChannel;
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

        return await _mesChannel
            .UploadRealtimeAsync(_deviceService.CurrentDevice, snapshot, cancellationToken)
            .ConfigureAwait(false);
    }

    protected override Task OnSnapshotUploadedAsync(
        HomogenizationRealtimeSnapshot snapshot,
        MesCallResult result,
        CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(_codeOptions.Mes.Channels.Realtime);
        }
        else
        {
            _diagnosticsStore.RecordFailure(_codeOptions.Mes.Channels.Realtime, result.Message);
        }

        _context.LastRealtimeAt = snapshot.CapturedAt;
        _context.LastRealtimeResult = result.Message;
        _context.LastRealtimeSnapshot = snapshot;
        return Task.CompletedTask;
    }
}
