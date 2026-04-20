using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Runtime.DataPipeline.Tasks;

public sealed class CloudRetryTask : ScheduledTaskBase
{
    private readonly ICloudRetryRecordStore _retryStore;
    private readonly ICloudFallbackBufferStore _fallbackStore;
    private readonly ICloudDeadLetterStore _deadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly IDeviceService _deviceService;
    private readonly ICloudConsumer _cloudConsumer;
    private readonly ICloudBatchConsumer _cloudBatchConsumer;
    private readonly IDeviceLogSyncTask _deviceLogSync;
    private readonly ICapacitySyncTask _capacitySync;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly IProcessIntegrationRegistry? _processIntegrationRegistry;
    private readonly DataPipelineCapacityGuard? _capacityGuard;
    private bool _wasUnavailable = true;
    private DateOnly? _lastAbandonedCleanupDateUtc;

    private const int MaxRetryCount = 20;
    private static readonly DateTime AbandonedRetryTimeUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
    private static readonly TimeSpan AbandonedRetention = TimeSpan.FromDays(30);

    public override string TaskName => "CloudRetryTask";
    protected override int ExecuteInterval => 5000;

    public CloudRetryTask(
        ILogService logger,
        ICloudRetryRecordStore retryStore,
        ICloudFallbackBufferStore fallbackStore,
        ICloudDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        IDeviceService deviceService,
        ICloudConsumer cloudConsumer,
        ICloudBatchConsumer cloudBatchConsumer,
        IDeviceLogSyncTask deviceLogSync,
        ICapacitySyncTask capacitySync,
        ICloudUploadDiagnosticsStore diagnosticsStore,
        IProcessIntegrationRegistry? processIntegrationRegistry = null,
        DataPipelineCapacityGuard? capacityGuard = null)
        : base(logger)
    {
        _retryStore = retryStore;
        _fallbackStore = fallbackStore;
        _deadLetterStore = deadLetterStore;
        _criticalFallbackWriter = criticalFallbackWriter;
        _deviceService = deviceService;
        _cloudConsumer = cloudConsumer;
        _cloudBatchConsumer = cloudBatchConsumer;
        _deviceLogSync = deviceLogSync;
        _capacitySync = capacitySync;
        _diagnosticsStore = diagnosticsStore;
        _processIntegrationRegistry = processIntegrationRegistry;
        _capacityGuard = capacityGuard;
    }

    internal Task ExecuteOneIterationAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ExecuteAsync().WaitAsync(ct);
    }

    protected override async Task ExecuteAsync()
    {
        if (!_deviceService.CanUploadToCloud)
        {
            _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
            _wasUnavailable = true;
            return;
        }

        _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.Retrying);

        if (_wasUnavailable)
        {
            _wasUnavailable = false;
            await RecoverAbandonedRecordsAsync().ConfigureAwait(false);
        }

        await CleanupExpiredAbandonedRecordsAsync().ConfigureAwait(false);
        await RecoverFallbackRecordsAsync().ConfigureAwait(false);

        var keepRetrying = await RetryFailedCellRecordsAsync().ConfigureAwait(false);
        if (!keepRetrying)
        {
            return;
        }

        var deviceLogSnapshotBefore = _diagnosticsStore.Snapshot;
        var retriedLogs = await _deviceLogSync.RetryBufferAsync().ConfigureAwait(false);
        if (!retriedLogs)
        {
            if (DidPauseForRecovery(deviceLogSnapshotBefore, "DeviceLog"))
            {
                _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
                return;
            }

            Logger.Warn("[Retry-Cloud] Device log buffer retry paused or failed.");
        }

        var capacitySnapshotBefore = _diagnosticsStore.Snapshot;
        var retriedCapacity = await _capacitySync.RetryBufferAsync().ConfigureAwait(false);
        if (!retriedCapacity)
        {
            if (DidPauseForRecovery(capacitySnapshotBefore, "Capacity"))
            {
                _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
                return;
            }

            Logger.Warn("[Retry-Cloud] Capacity buffer retry paused or failed.");
        }

        if (_capacityGuard is not null)
        {
            await _capacityGuard.RefreshCloudRetryCapacityStatusAsync().ConfigureAwait(false);
            await _capacityGuard.RefreshCloudFallbackCapacityStatusAsync().ConfigureAwait(false);
        }

        await ApplyIdleOrBackoffStateAsync().ConfigureAwait(false);
    }

    private async Task RecoverAbandonedRecordsAsync()
    {
        try
        {
            await _retryStore.ResetAllAbandonedAsync().ConfigureAwait(false);
            Logger.Info("[Retry-Cloud] Upload gate recovered. Abandoned records were reset for retry.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Retry-Cloud] Failed to reset abandoned records: {ex.Message}");
        }
    }

    private async Task RecoverFallbackRecordsAsync()
    {
        var pending = await _fallbackStore.GetPendingAsync().ConfigureAwait(false);
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
                var persisted = await TryPersistDeadLetterAsync(
                    fallback.ProcessType,
                    fallback.CellDataJson,
                    fallback.FailedTarget,
                    sourceTable: "cloud_fallback_records",
                    sourceRecordId: fallback.Id,
                    DeadLetterStage.FallbackRecoverDeserialize,
                    $"Cloud fallback deserialize failed for process type {fallback.ProcessType}.").ConfigureAwait(false);

                if (persisted)
                {
                    recoveredIds.Add(fallback.Id);
                }

                continue;
            }

            try
            {
                var retryBlockedReason = _capacityGuard is null
                    ? null
                    : await _capacityGuard.GetCloudRetryBlockReasonAsync(fallback.ProcessType).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(retryBlockedReason))
                {
                    Logger.Warn(
                        $"[Retry-Cloud] Cloud fallback record {fallback.Id} remains buffered because retry capacity is blocked by {retryBlockedReason}.");
                    continue;
                }

                await _retryStore.SaveAsync(
                    new CellCompletedRecord { CellData = cellData },
                    fallback.FailedTarget,
                    fallback.ErrorMessage).ConfigureAwait(false);
                recoveredIds.Add(fallback.Id);
            }
            catch (Exception ex)
            {
                Logger.Error($"[Retry-Cloud] Failed to rehydrate Cloud fallback record {fallback.Id}: {ex.Message}");
            }
        }

        if (recoveredIds.Count > 0)
        {
            await _fallbackStore.DeleteBatchAsync(recoveredIds).ConfigureAwait(false);
            Logger.Info($"[Retry-Cloud] Recovered {recoveredIds.Count} Cloud fallback record(s) into the main retry store.");
        }

        if (_capacityGuard is not null)
        {
            await _capacityGuard.RefreshCloudFallbackCapacityStatusAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> RetryFailedCellRecordsAsync()
    {
        var records = await _retryStore.GetPendingAsync(batchSize: 100).ConfigureAwait(false);
        if (records.Count == 0)
        {
            return true;
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
                        var persisted = await TryPersistDeadLetterAsync(
                            source.ProcessType,
                            source.CellDataJson,
                            source.FailedTarget,
                            sourceTable: "failed_cloud_records",
                            sourceRecordId: source.Id,
                            DeadLetterStage.RetryDeserialize,
                            $"Cloud retry deserialize failed for process type {source.ProcessType}.").ConfigureAwait(false);

                        if (persisted)
                        {
                            await _retryStore.DeleteAsync(source.Id).ConfigureAwait(false);
                        }

                        continue;
                    }

                    completedRecords.Add(new CellCompletedRecord { CellData = cellData });
                    validSourceRecords.Add(source);
                }

                if (completedRecords.Count == 0)
                {
                    continue;
                }

                var result = await _cloudBatchConsumer.ProcessBatchAsync(completedRecords).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    foreach (var source in validSourceRecords)
                    {
                        await _retryStore.DeleteAsync(source.Id).ConfigureAwait(false);
                    }

                    Logger.Info($"[Retry-Cloud] {processGroup.Key} batch retry succeeded. Count:{validSourceRecords.Count}");
                    continue;
                }

                if (ShouldPauseForRecovery(result))
                {
                    _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
                    Logger.Warn($"[Retry-Cloud] {processGroup.Key} batch retry paused. Outcome:{result.Outcome}, Reason:{result.ReasonCode}");
                    return false;
                }

                foreach (var source in validSourceRecords)
                {
                    await HandleRetryFailureAsync(source, $"Cloud batch retry failed ({result.ReasonCode}).").ConfigureAwait(false);
                }

                Logger.Warn($"[Retry-Cloud] {processGroup.Key} batch retry failed. Count:{validSourceRecords.Count}");
            }
        }

        foreach (var record in others)
        {
            var keepRetrying = await ProcessOneAsync(record).ConfigureAwait(false);
            if (!keepRetrying)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsCloudBatchRetryCandidate(FailedCellRecord record)
        => ResolveUploadMode(record.ProcessType) == ProcessUploadMode.Batch;

    private ProcessUploadMode ResolveUploadMode(string processType)
    {
        if (_processIntegrationRegistry?.TryGetCloudUploader(processType, out var registration) == true)
        {
            return registration.UploadMode;
        }

        return ProcessUploadMode.Single;
    }

    private async Task<bool> ProcessOneAsync(FailedCellRecord record)
    {
        var cellData = DeserializeCellData(record.ProcessType, record.CellDataJson);
        if (cellData is null)
        {
            var persisted = await TryPersistDeadLetterAsync(
                record.ProcessType,
                record.CellDataJson,
                record.FailedTarget,
                sourceTable: "failed_cloud_records",
                sourceRecordId: record.Id,
                DeadLetterStage.RetryDeserialize,
                $"Cloud retry deserialize failed for process type {record.ProcessType}.").ConfigureAwait(false);

            if (persisted)
            {
                await _retryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            }

            return true;
        }

        var result = await _cloudConsumer
            .ProcessWithResultAsync(new CellCompletedRecord { CellData = cellData })
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await _retryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            Logger.Info($"[Retry-Cloud] {cellData.DisplayLabel} retry succeeded and the record was removed.");
            return true;
        }

        if (ShouldPauseForRecovery(result))
        {
            _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
            Logger.Warn($"[Retry-Cloud] {cellData.DisplayLabel} retry paused. Outcome:{result.Outcome}, Reason:{result.ReasonCode}");
            return false;
        }

        await HandleRetryFailureAsync(record, result.ReasonCode).ConfigureAwait(false);
        return true;
    }

    private async Task HandleRetryFailureAsync(FailedCellRecord record, string errorMessage)
    {
        var newRetryCount = record.RetryCount + 1;
        _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.Backoff);

        if (newRetryCount > MaxRetryCount)
        {
            Logger.Warn($"[Retry-Cloud] {record.ProcessType} reached max retry count {MaxRetryCount}. Auto retry stopped.");
            await _retryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, AbandonedRetryTimeUtc).ConfigureAwait(false);
            return;
        }

        var nextRetryTime = DateTime.UtcNow.Add(CalculateBackoff(newRetryCount));
        await _retryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, nextRetryTime).ConfigureAwait(false);
    }

    private async Task CleanupExpiredAbandonedRecordsAsync()
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_lastAbandonedCleanupDateUtc == todayUtc)
        {
            return;
        }

        _lastAbandonedCleanupDateUtc = todayUtc;

        try
        {
            var deleted = await _retryStore
                .DeleteExpiredAbandonedAsync(DateTime.UtcNow.Subtract(AbandonedRetention))
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                Logger.Info($"[Retry-Cloud] Deleted {deleted} expired abandoned retry record(s).");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Retry-Cloud] Failed to cleanup expired abandoned records: {ex.Message}");
        }
    }

    private static bool ShouldPauseForRecovery(CloudCallResult result)
        => result.Outcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry;

    private static bool ShouldPauseForRecovery(CloudUploadDiagnosticsSnapshot snapshot)
        => snapshot.LastOutcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry;

    private bool DidPauseForRecovery(CloudUploadDiagnosticsSnapshot previousSnapshot, string processType)
    {
        var currentSnapshot = _diagnosticsStore.Snapshot;
        if (!string.Equals(currentSnapshot.LastProcessType, processType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (currentSnapshot.LastAttemptAt == previousSnapshot.LastAttemptAt)
        {
            return false;
        }

        return ShouldPauseForRecovery(currentSnapshot);
    }

    private async Task ApplyIdleOrBackoffStateAsync()
    {
        var pendingCount = await _retryStore.GetCountAsync().ConfigureAwait(false);
        _diagnosticsStore.SetRuntimeState(
            pendingCount > 0
                ? CloudRetryRuntimeState.Backoff
                : CloudRetryRuntimeState.Idle);
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
            return CellDataJsonSerializer.Deserialize(processType, json);
        }
        catch (Exception ex)
        {
            Logger.Error($"[Retry-Cloud] CellData deserialize failed: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> TryPersistDeadLetterAsync(
        string processType,
        string cellDataJson,
        string failedTarget,
        string sourceTable,
        long sourceRecordId,
        DeadLetterStage stage,
        string failureReason)
    {
        try
        {
            await _deadLetterStore.SaveAsync(new DeadLetterRecord
            {
                ProcessType = processType,
                CellDataJson = cellDataJson,
                FailedTarget = failedTarget,
                SourceTable = sourceTable,
                SourceRecordId = sourceRecordId,
                FailureStage = stage.ToString(),
                FailureReason = failureReason,
                CreatedAt = DateTime.UtcNow
            }).ConfigureAwait(false);

            Logger.Fatal($"[Retry-Cloud] {processType} record {sourceRecordId} moved into Cloud dead-letter store.");
            return true;
        }
        catch (Exception ex)
        {
            _criticalFallbackWriter.Write(
                "Retry.CloudDeadLetterPersistFailed",
                $"{failureReason} Dead-letter save failed: {ex.Message}",
                ex);
            return false;
        }
    }
}
