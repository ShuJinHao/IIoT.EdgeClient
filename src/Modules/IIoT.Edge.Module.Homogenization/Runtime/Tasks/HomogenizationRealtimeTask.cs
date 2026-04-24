using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Runtime.Base;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

internal sealed class HomogenizationRealtimeTask : PeriodicSnapshotUploadTaskBase<HomogenizationRealtimeSnapshot>
{
    private readonly HomogenizationContext _context;
    private readonly IDeviceService _deviceService;
    private readonly IHomogenizationMesApiService _mesApiService;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly HomogenizationCodeOptions _codeOptions;
    private readonly int _taskLoopInterval;
    private HomogenizationSignalCodec? _codec;

    public HomogenizationRealtimeTask(
        IPlcBuffer buffer,
        HomogenizationContext context,
        IDeviceService deviceService,
        IHomogenizationMesApiService mesApiService,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger,
        HomogenizationModuleOptions moduleOptions,
        HomogenizationCodeOptions codeOptions)
        : base(buffer, context, logger)
    {
        _context = context;
        _deviceService = deviceService;
        _mesApiService = mesApiService;
        _diagnosticsStore = diagnosticsStore;
        _codeOptions = codeOptions;
        _taskLoopInterval = Math.Max(200, moduleOptions.Runtime.RealtimeLoopIntervalMs);
    }

    public override string TaskName => "Homogenization.Realtime";

    protected override int TaskLoopInterval => _taskLoopInterval;

    protected override HomogenizationRealtimeSnapshot CaptureSnapshot()
        => Codec.CaptureRealtimeSnapshot();

    protected override Task<MesCallResult> UploadSnapshotAsync(
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken)
        => _mesApiService.UploadRealtimeAsync(_deviceService.CurrentDevice, snapshot, cancellationToken);

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

    private HomogenizationSignalCodec Codec => _codec ??= new HomogenizationSignalCodec(Buffer, _context);
}
