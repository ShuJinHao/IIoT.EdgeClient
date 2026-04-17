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
    private readonly Dictionary<string, IProcessCloudUploader> _uploaders;

    public string? RetryChannel => "Cloud";
    public string Name => "Cloud";
    public int Order => 20;
    public IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode.Durable;

    public CloudConsumer(
        IDeviceService deviceService,
        IEnumerable<IProcessCloudUploader> uploaders,
        ILogService logger)
    {
        _deviceService = deviceService;
        _logger = logger;
        _uploaders = uploaders.ToDictionary(x => x.ProcessType, StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> ProcessAsync(CellCompletedRecord record) => ProcessBatchAsync([record]);

    public async Task<bool> ProcessBatchAsync(IReadOnlyList<CellCompletedRecord> records)
    {
        if (records.Count == 0)
        {
            return true;
        }

        var device = _deviceService.CurrentDevice;
        if (device is null)
        {
            _logger.Warn("[Cloud] Device is not identified yet. Move record(s) to retry queue.");
            return false;
        }

        if (_deviceService.CurrentState == NetworkState.Offline)
        {
            _logger.Warn($"[Cloud] Network is offline. Move {records.Count} record(s) to retry queue.");
            return false;
        }

        var context = new ProcessCloudUploadContext(device);
        foreach (var group in records.GroupBy(x => x.CellData.ProcessType, StringComparer.OrdinalIgnoreCase))
        {
            if (!_uploaders.TryGetValue(group.Key, out var uploader))
            {
                _logger.Error($"[Cloud] No uploader registered for process type: {group.Key}");
                return false;
            }

            var success = await uploader.UploadAsync(context, group.ToList()).ConfigureAwait(false);
            if (!success)
            {
                _logger.Error($"[Cloud] Upload failed for process type {group.Key}. Count:{group.Count()}");
                return false;
            }
        }

        return true;
    }
}
