using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using System.Text.Json;

namespace IIoT.Edge.Runtime.DataPipeline.Tasks;

public class RetryTask : ScheduledTaskBase
{
    private readonly string _channel;
    private readonly IFailedRecordStore _failedStore;
    private readonly ICloudFallbackBufferStore? _cloudFallbackStore;
    private readonly IDeviceService _deviceService;
    private readonly List<ICellDataConsumer> _consumers;
    private readonly ICloudBatchConsumer? _cloudBatchConsumer;
    private readonly IDeviceLogSyncTask? _deviceLogSync;
    private readonly ICapacitySyncTask? _capacitySync;
    private readonly IProcessIntegrationRegistry? _processIntegrationRegistry;
    private bool _wasOffline = true;

    private const int MaxRetryCount = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override string TaskName => $"RetryTask[{_channel}]";
    protected override int ExecuteInterval => 5000;

    public RetryTask(
        string channel,
        ILogService logger,
        IFailedRecordStore failedStore,
        IDeviceService deviceService,
        IEnumerable<ICellDataConsumer> consumers,
        IDeviceLogSyncTask? deviceLogSync = null,
        ICapacitySyncTask? capacitySync = null,
        ICloudBatchConsumer? cloudBatchConsumer = null,
        ICloudFallbackBufferStore? cloudFallbackStore = null,
        IProcessIntegrationRegistry? processIntegrationRegistry = null)
        : base(logger)
    {
        _channel = channel;
        _failedStore = failedStore;
        _deviceService = deviceService;
        _consumers = consumers.OrderBy(c => c.Order).ToList();
        _deviceLogSync = deviceLogSync;
        _capacitySync = capacitySync;
        _cloudBatchConsumer = cloudBatchConsumer;
        _cloudFallbackStore = cloudFallbackStore;
        _processIntegrationRegistry = processIntegrationRegistry;
    }

    protected override async Task ExecuteAsync()
    {
        if (_channel == "Cloud")
        {
            var cloudReady = _deviceService.CurrentState == NetworkState.Online && _deviceService.HasDeviceId;
            if (!cloudReady)
            {
                _wasOffline = true;
                return;
            }

            if (_wasOffline)
            {
                _wasOffline = false;
                await RecoverAbandonedRecordsAsync();
            }

            await RecoverCloudFallbackRecordsAsync();
        }

        await RetryFailedCellRecordsAsync();

        if (_channel == "Cloud" && _deviceLogSync is not null)
        {
            var retried = await _deviceLogSync.RetryBufferAsync();
            if (!retried)
            {
                Logger.Warn($"[Retry-{_channel}] Device log buffer retry did not fully succeed.");
            }
        }

        if (_channel == "Cloud" && _capacitySync is not null)
        {
            var retried = await _capacitySync.RetryBufferAsync();
            if (!retried)
            {
                Logger.Warn($"[Retry-{_channel}] Capacity buffer retry did not fully succeed.");
            }
        }
    }

    private async Task RecoverAbandonedRecordsAsync()
    {
        try
        {
            await _failedStore.ResetAllAbandonedAsync().ConfigureAwait(false);
            Logger.Info($"[Retry-{_channel}] Network recovered. Abandoned records were reset for retry.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Retry-{_channel}] Failed to reset abandoned records: {ex.Message}");
        }
    }

    private async Task RecoverCloudFallbackRecordsAsync()
    {
        if (_cloudFallbackStore is null)
        {
            return;
        }

        var pending = await _cloudFallbackStore.GetPendingAsync().ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        var recoveredIds = new List<long>();
        foreach (var fallback in pending)
        {
            var cellData = DeserializeCellData(fallback.ProcessType, fallback.CellDataJson);
            if (cellData is null)
            {
                Logger.Error($"[Retry-{_channel}] Cloud fallback deserialize failed for process type {fallback.ProcessType}. Delete record.");
                recoveredIds.Add(fallback.Id);
                continue;
            }

            try
            {
                await _failedStore.SaveAsync(
                    new CellCompletedRecord { CellData = cellData },
                    fallback.FailedTarget,
                    fallback.ErrorMessage,
                    "Cloud").ConfigureAwait(false);
                recoveredIds.Add(fallback.Id);
            }
            catch (Exception ex)
            {
                Logger.Error($"[Retry-{_channel}] Failed to rehydrate Cloud fallback record {fallback.Id}: {ex.Message}");
                break;
            }
        }

        if (recoveredIds.Count > 0)
        {
            await _cloudFallbackStore.DeleteBatchAsync(recoveredIds).ConfigureAwait(false);
            Logger.Info($"[Retry-{_channel}] Recovered {recoveredIds.Count} Cloud fallback record(s) into the main retry store.");
        }
    }

    private async Task RetryFailedCellRecordsAsync()
    {
        if (_channel == "Cloud" && _cloudBatchConsumer is not null)
        {
            await RetryCloudBatchesAsync();
            return;
        }

        var records = await _failedStore.GetPendingAsync(_channel, batchSize: 5).ConfigureAwait(false);
        if (records.Count == 0)
        {
            return;
        }

        foreach (var record in records)
        {
            await ProcessOneAsync(record).ConfigureAwait(false);
        }
    }

    private async Task RetryCloudBatchesAsync()
    {
        var records = await _failedStore.GetPendingAsync(_channel, batchSize: 100).ConfigureAwait(false);
        if (records.Count == 0)
        {
            return;
        }

        var batchCandidates = records
            .Where(IsCloudBatchRetryCandidate)
            .ToList();

        var others = records
            .Where(r => !IsCloudBatchRetryCandidate(r))
            .ToList();

        foreach (var processGroup in batchCandidates.GroupBy(x => x.ProcessType, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var chunk in processGroup.Chunk(100))
            {
                var completedRecords = new List<CellCompletedRecord>();
                var validSourceRecords = new List<FailedCellRecord>();

                foreach (var source in chunk)
                {
                    var cellData = DeserializeCellData(source.ProcessType, source.CellDataJson);
                    if (cellData is null)
                    {
                        Logger.Error($"[Retry-{_channel}] Deserialize failed for process type {source.ProcessType}. Delete record.");
                        await _failedStore.DeleteAsync(source.Id).ConfigureAwait(false);
                        continue;
                    }

                    completedRecords.Add(new CellCompletedRecord { CellData = cellData });
                    validSourceRecords.Add(source);
                }

                if (completedRecords.Count == 0)
                {
                    continue;
                }

                var success = await _cloudBatchConsumer!.ProcessBatchAsync(completedRecords).ConfigureAwait(false);
                if (success)
                {
                    foreach (var source in validSourceRecords)
                    {
                        await _failedStore.DeleteAsync(source.Id).ConfigureAwait(false);
                    }

                    Logger.Info($"[Retry-{_channel}] {processGroup.Key} batch retry succeeded. Count:{validSourceRecords.Count}");
                    continue;
                }

                foreach (var source in validSourceRecords)
                {
                    await HandleRetryFailureAsync(source, "Cloud batch retry failed.").ConfigureAwait(false);
                }

                Logger.Warn($"[Retry-{_channel}] {processGroup.Key} batch retry failed. Count:{validSourceRecords.Count}");
                goto RetrySingles;
            }
        }

RetrySingles:
        foreach (var record in others)
        {
            await ProcessOneAsync(record).ConfigureAwait(false);
        }
    }

    private bool IsCloudBatchRetryCandidate(FailedCellRecord record)
    {
        return record.FailedTarget == "Cloud"
            && ResolveUploadMode(record.ProcessType) == ProcessUploadMode.Batch;
    }

    private ProcessUploadMode ResolveUploadMode(string processType)
    {
        if (_processIntegrationRegistry?.TryGetCloudUploader(processType, out var registration) == true)
        {
            return registration.UploadMode;
        }

        return string.Equals(processType, "Injection", StringComparison.OrdinalIgnoreCase)
            ? ProcessUploadMode.Batch
            : ProcessUploadMode.Single;
    }

    private async Task ProcessOneAsync(FailedCellRecord record)
    {
        var startIndex = _consumers.FindIndex(c => c.Name == record.FailedTarget);
        if (startIndex < 0)
        {
            Logger.Warn($"[Retry-{_channel}] Consumer {record.FailedTarget} was not found. Delete record.");
            await _failedStore.DeleteAsync(record.Id).ConfigureAwait(false);
            return;
        }

        var cellData = DeserializeCellData(record.ProcessType, record.CellDataJson);
        if (cellData is null)
        {
            Logger.Error($"[Retry-{_channel}] Deserialize failed for process type {record.ProcessType}. Delete record.");
            await _failedStore.DeleteAsync(record.Id).ConfigureAwait(false);
            return;
        }

        var completedRecord = new CellCompletedRecord { CellData = cellData };
        var label = cellData.DisplayLabel;

        for (var i = startIndex; i < _consumers.Count; i++)
        {
            var consumer = _consumers[i];
            if (consumer.RetryChannel != _channel)
            {
                continue;
            }

            try
            {
                var success = await consumer.ProcessAsync(completedRecord).ConfigureAwait(false);
                if (!success)
                {
                    Logger.Warn($"[Retry-{_channel}] {label} still failed at {consumer.Name}.");
                    await HandleRetryFailureAsync(record, "Consumer returned false.").ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[Retry-{_channel}] {label} failed at {consumer.Name}: {ex.Message}");
                await HandleRetryFailureAsync(record, ex.Message).ConfigureAwait(false);
                return;
            }
        }

        await _failedStore.DeleteAsync(record.Id).ConfigureAwait(false);
        Logger.Info($"[Retry-{_channel}] {label} retry succeeded and the record was removed.");
    }

    private async Task HandleRetryFailureAsync(FailedCellRecord record, string errorMessage)
    {
        var newRetryCount = record.RetryCount + 1;

        if (newRetryCount > MaxRetryCount)
        {
            Logger.Warn($"[Retry-{_channel}] {record.ProcessType} reached max retry count {MaxRetryCount}. Auto retry stopped.");
            await _failedStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, DateTime.MaxValue).ConfigureAwait(false);
            return;
        }

        var nextRetryTime = DateTime.Now.Add(CalculateBackoff(newRetryCount));
        await _failedStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, nextRetryTime).ConfigureAwait(false);
    }

    private static TimeSpan CalculateBackoff(int retryCount)
    {
        if (retryCount <= 5)
        {
            return TimeSpan.FromSeconds(30);
        }

        if (retryCount <= 10)
        {
            return TimeSpan.FromMinutes(5);
        }

        return TimeSpan.FromMinutes(30);
    }

    private CellDataBase? DeserializeCellData(string processType, string json)
    {
        try
        {
            return CellDataTypeRegistry.Deserialize(processType, json, JsonOptions);
        }
        catch (Exception ex)
        {
            Logger.Error($"[Retry-{_channel}] CellData deserialize failed: {ex.Message}");
            return null;
        }
    }
}
