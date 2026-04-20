using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public class CloudConsumer : ICloudConsumer, ICloudBatchConsumer
{
    private readonly IDeviceService _deviceService;
    private readonly ILogService _logger;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly Dictionary<string, IProcessCloudUploader> _uploaders;

    public string? RetryChannel => "Cloud";
    public string Name => "Cloud";
    public int Order => 20;
    public IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode.Durable;

    public CloudConsumer(
        IDeviceService deviceService,
        IEnumerable<IProcessCloudUploader> uploaders,
        ICloudUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
    {
        _deviceService = deviceService;
        _diagnosticsStore = diagnosticsStore;
        _logger = logger;
        _uploaders = uploaders.ToDictionary(x => x.ProcessType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record)
        => (await ProcessWithResultAsync(record).ConfigureAwait(false)).IsSuccess;

    public Task<CloudCallResult> ProcessWithResultAsync(CellCompletedRecord record)
        => ProcessBatchAsync([record]);

    public async Task<CloudCallResult> ProcessBatchAsync(IReadOnlyList<CellCompletedRecord> records)
    {
        if (records.Count == 0)
        {
            return CloudCallResult.Success();
        }

        if (!_deviceService.CanUploadToCloud)
        {
            var blockedResult = CloudCallResult.Failure(
                CloudCallOutcome.SkippedUploadNotReady,
                _deviceService.CurrentUploadGate.Reason.ToReasonCode());
            _logger.Warn(
                $"[Cloud] Upload gate is blocked ({_deviceService.CurrentUploadGate.Reason.ToReasonCode()}). Move {records.Count} record(s) to retry queue.");
            _diagnosticsStore.RecordResult(records[0].CellData.ProcessType, blockedResult);
            return blockedResult;
        }

        var device = _deviceService.CurrentDevice;
        if (device is null)
        {
            var unidentifiedResult = CloudCallResult.Failure(
                CloudCallOutcome.SkippedUploadNotReady,
                EdgeUploadBlockReason.DeviceUnidentified.ToReasonCode());
            _logger.Warn("[Cloud] Device is not identified yet. Move record(s) to retry queue.");
            _diagnosticsStore.RecordResult(records[0].CellData.ProcessType, unidentifiedResult);
            return unidentifiedResult;
        }

        var context = new ProcessCloudUploadContext(device);
        foreach (var group in records.GroupBy(x => x.CellData.ProcessType, StringComparer.OrdinalIgnoreCase))
        {
            if (!_uploaders.TryGetValue(group.Key, out var uploader))
            {
                var uploaderMissing = CloudCallResult.Failure(CloudCallOutcome.Exception, "uploader_not_found");
                _logger.Error($"[Cloud] No uploader registered for process type: {group.Key}");
                _diagnosticsStore.RecordResult(group.Key, uploaderMissing);
                return uploaderMissing;
            }

            var result = await uploader.UploadAsync(context, group.ToList()).ConfigureAwait(false);
            _diagnosticsStore.RecordResult(group.Key, result);
            if (!result.IsSuccess)
            {
                _logger.Error(
                    $"[Cloud] Upload failed for process type {group.Key}. Count:{group.Count()}, Outcome:{result.Outcome}, Reason:{result.ReasonCode}");
                return result;
            }
        }

        return CloudCallResult.Success();
    }
}
