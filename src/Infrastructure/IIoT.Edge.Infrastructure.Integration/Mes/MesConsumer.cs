using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Infrastructure.Integration.Mes;

public sealed class MesConsumer : IMesConsumer
{
    private readonly IDeviceService _deviceService;
    private readonly ILogService _logger;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly Dictionary<string, IProcessMesUploader> _uploaders;

    public string Name => "MES";
    public int Order => 20;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;
    public string? RetryChannel => "MES";

    public MesConsumer(
        IDeviceService deviceService,
        IEnumerable<IProcessMesUploader> uploaders,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
    {
        _deviceService = deviceService;
        _diagnosticsStore = diagnosticsStore;
        _logger = logger;
        _uploaders = uploaders.ToDictionary(x => x.ProcessType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record)
    {
        if (!_uploaders.TryGetValue(record.CellData.ProcessType, out var uploader))
        {
            return true;
        }

        var device = _deviceService.CurrentDevice;
        if (device is null)
        {
            const string reason = "Device is not identified yet.";
            _diagnosticsStore.RecordFailure(record.CellData.ProcessType, reason);
            _logger.Warn($"[MES] {reason} ProcessType={record.CellData.ProcessType}");
            return false;
        }

        var success = await uploader
            .UploadAsync(new ProcessMesUploadContext(device), [record])
            .ConfigureAwait(false);

        if (success)
        {
            _diagnosticsStore.RecordSuccess(record.CellData.ProcessType);
            return true;
        }

        const string failedReason = "Uploader returned false.";
        _diagnosticsStore.RecordFailure(record.CellData.ProcessType, failedReason);
        _logger.Error($"[MES] Upload failed for process type {record.CellData.ProcessType}.");
        return false;
    }
}
