using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Runtime.Base;
using Microsoft.Extensions.Options;
using HomogenizationMesScenarioChannel = IIoT.Edge.Application.Modules.Mes.IMesScenarioChannel<
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationCellData,
    string,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRealtimeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRecipeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationEquipmentStatusSnapshot>;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

/// <summary>
/// 实时上传任务：周期采集 PLC 实时数据快照并上传 MES。
/// </summary>
internal sealed class HomogenizationRealtimeTask : PeriodicSnapshotUploadTaskBase<HomogenizationRealtimeSnapshot>
{
    private readonly HomogenizationContext _context;
    private readonly IDeviceService _deviceService;
    private readonly HomogenizationMesScenarioChannel _mesChannel;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly HomogenizationCodeOptions _codeOptions;
    private readonly int _taskLoopInterval;
    private readonly HomogenizationSignalCodec _codec;

    public HomogenizationRealtimeTask(
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
        : base(buffer, context, logger)
    {
        _context = context;
        _deviceService = deviceService;
        _mesChannel = mesChannel;
        _diagnosticsStore = diagnosticsStore;
        _codeOptions = codeOptions.Value;
        _codec = new HomogenizationSignalCodec(signals, productionTime);
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
        => Codec.CaptureRealtimeSnapshot();

    protected override Task<MesCallResult> UploadSnapshotAsync(
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken)
        => _mesChannel.UploadRealtimeAsync(_deviceService.CurrentDevice, snapshot, cancellationToken);

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

    private HomogenizationSignalCodec Codec => _codec;
}
