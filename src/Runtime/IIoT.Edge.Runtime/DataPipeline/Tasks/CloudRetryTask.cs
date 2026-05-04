using IIoT.Edge.Application.Abstractions.DataPipeline;
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
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel = new(
        LogPrefix: "Retry-Cloud",
        DeadLetterName: "Cloud",
        CriticalSource: "Retry.CloudDeadLetterPersistFailed");

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
    private readonly DataPipelineCapacityGuard _capacityGuard;
    private readonly TimeSpan _consumerCallTimeout;
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
        DataPipelineCapacityGuard capacityGuard,
        IProcessIntegrationRegistry? processIntegrationRegistry = null,
        DataPipelineRuntimeOptions? runtimeOptions = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(capacityGuard);

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
        _consumerCallTimeout = (runtimeOptions ?? new DataPipelineRuntimeOptions()).GetConsumerCallTimeout();
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

            Logger.Warn("[Retry-Cloud] 设备日志缓冲补传已暂停或失败。");
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

            Logger.Warn("[Retry-Cloud] 产能缓冲补传已暂停或失败。");
        }

        await _capacityGuard.RefreshCloudRetryCapacityStatusAsync().ConfigureAwait(false);
        await _capacityGuard.RefreshCloudFallbackCapacityStatusAsync().ConfigureAwait(false);

        await ApplyIdleOrBackoffStateAsync().ConfigureAwait(false);
    }

    private async Task RecoverAbandonedRecordsAsync()
    {
        try
        {
            await _retryStore.ResetAllAbandonedAsync().ConfigureAwait(false);
            Logger.Info("[Retry-Cloud] 云端上传门控已恢复，弃置记录已重置为可补传。");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Retry-Cloud] 重置弃置记录失败：{ex.Message}");
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
        var deadLetterIds = new List<long>();
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
                    $"Cloud fallback 记录反序列化失败，工序：{fallback.ProcessType}。").ConfigureAwait(false);

                if (persisted)
                {
                    deadLetterIds.Add(fallback.Id);
                }

                continue;
            }

            try
            {
                var retryBlockedReason = await _capacityGuard
                    .GetCloudRetryBlockReasonAsync(fallback.ProcessType)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(retryBlockedReason))
                {
                    Logger.Warn(
                        $"[Retry-Cloud] Cloud fallback 记录 {fallback.Id} 因 retry 容量阻塞继续保留，原因：{retryBlockedReason}。");
                    continue;
                }

                recoveredIds.Add(fallback.Id);
            }
            catch (Exception ex)
            {
                Logger.Error($"[Retry-Cloud] 恢复 Cloud fallback 记录 {fallback.Id} 失败：{ex.Message}");
            }
        }

        if (deadLetterIds.Count > 0)
        {
            await _fallbackStore.DeleteBatchAsync(deadLetterIds).ConfigureAwait(false);
        }

        if (recoveredIds.Count > 0)
        {
            await _fallbackStore.MovePendingToRetryAsync(recoveredIds).ConfigureAwait(false);
            Logger.Info($"[Retry-Cloud] 已将 {recoveredIds.Count} 条 Cloud fallback 记录恢复到 retry 主表。");
        }

        await _capacityGuard.RefreshCloudFallbackCapacityStatusAsync().ConfigureAwait(false);
    }

    private async Task<bool> RetryFailedCellRecordsAsync()
    {
        var claimedBatch = await _retryStore.ClaimPendingBatchAsync(batchSize: 100).ConfigureAwait(false);
        if (claimedBatch is null || claimedBatch.Records.Count == 0)
        {
            return true;
        }

        var records = claimedBatch.Records;
        var batchCandidates = records
            .Where(IsCloudBatchRetryCandidate)
            .ToList();

        var others = records
            .Where(r => !IsCloudBatchRetryCandidate(r))
            .ToList();

        try
        {
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
                                $"Cloud retry 记录反序列化失败，工序：{source.ProcessType}。").ConfigureAwait(false);

                            if (persisted)
                            {
                                await _retryStore.DeleteAsync(source.Id).ConfigureAwait(false);
                            }
                            else
                            {
                                await HandleRetryFailureAsync(
                                    source,
                                    "Cloud retry 记录反序列化失败，且死信持久化也失败。").ConfigureAwait(false);
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

                    CloudCallResult result;
                    try
                    {
                        result = await DataPipelineConsumerCall
                            .ExecuteAsync(
                                ct => _cloudBatchConsumer.ProcessBatchAsync(completedRecords, ct),
                                _consumerCallTimeout,
                                CurrentCancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        foreach (var source in validSourceRecords)
                        {
                            await HandleRetryFailureAsync(source, "timeout_exceeded").ConfigureAwait(false);
                        }

                        Logger.Warn($"[Retry-Cloud] {processGroup.Key} 批量补传超时，数量：{validSourceRecords.Count}。");
                        continue;
                    }

                    if (result.IsSuccess)
                    {
                        foreach (var source in validSourceRecords)
                        {
                            await _retryStore.DeleteAsync(source.Id).ConfigureAwait(false);
                        }

                        Logger.Info($"[Retry-Cloud] {processGroup.Key} 批量补传成功，数量：{validSourceRecords.Count}。");
                        continue;
                    }

                    if (ShouldPauseForRecovery(result))
                    {
                        await _retryStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                        _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
                        Logger.Warn($"[Retry-Cloud] {processGroup.Key} 批量补传已暂停，结果：{result.Outcome}，原因：{result.ReasonCode}。");
                        return false;
                    }

                    foreach (var source in validSourceRecords)
                    {
                        await HandleRetryFailureAsync(source, $"Cloud 批量补传失败（{result.ReasonCode}）。").ConfigureAwait(false);
                    }

                    Logger.Warn($"[Retry-Cloud] {processGroup.Key} 批量补传失败，数量：{validSourceRecords.Count}。");
                }
            }

            foreach (var record in others)
            {
                var processResult = await ProcessOneAsync(record).ConfigureAwait(false);
                if (processResult == RetryProcessResult.Pause)
                {
                    await _retryStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                    _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            try
            {
                await _retryStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
            }
            catch (Exception releaseEx)
            {
                Logger.Error($"[Retry-Cloud] 释放 retry 领取标记 {claimedBatch.ClaimToken} 失败：{releaseEx.Message}");
            }

            Logger.Error($"[Retry-Cloud] retry 批次执行异常：{ex.Message}");
            return false;
        }
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

    private async Task<RetryProcessResult> ProcessOneAsync(FailedCellRecord record)
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
                $"Cloud retry 记录反序列化失败，工序：{record.ProcessType}。").ConfigureAwait(false);

            if (persisted)
            {
                await _retryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            }
            else
            {
                await HandleRetryFailureAsync(
                    record,
                    "Cloud retry 记录反序列化失败，且死信持久化也失败。").ConfigureAwait(false);
            }

            return RetryProcessResult.Continue;
        }

        CloudCallResult result;
        try
        {
            result = await DataPipelineConsumerCall
                .ExecuteAsync(
                    ct => _cloudConsumer.ProcessWithResultAsync(new CellCompletedRecord { CellData = cellData }, ct),
                    _consumerCallTimeout,
                    CurrentCancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await HandleRetryFailureAsync(record, "timeout_exceeded").ConfigureAwait(false);
            return RetryProcessResult.Continue;
        }

        if (result.IsSuccess)
        {
            await _retryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            Logger.Info($"[Retry-Cloud] {cellData.DisplayLabel} 补传成功，记录已删除。");
            return RetryProcessResult.Continue;
        }

        if (ShouldPauseForRecovery(result))
        {
            Logger.Warn($"[Retry-Cloud] {cellData.DisplayLabel} 补传已暂停，结果：{result.Outcome}，原因：{result.ReasonCode}。");
            return RetryProcessResult.Pause;
        }

        await HandleRetryFailureAsync(record, result.ReasonCode).ConfigureAwait(false);
        return RetryProcessResult.Continue;
    }

    private async Task HandleRetryFailureAsync(FailedCellRecord record, string errorMessage)
    {
        var newRetryCount = record.RetryCount + 1;
        _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.Backoff);

        if (newRetryCount > MaxRetryCount)
        {
            Logger.Warn($"[Retry-Cloud] {record.ProcessType} 已达到最大补传次数 {MaxRetryCount}，自动补传停止。");
            await _retryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, AbandonedRetryTimeUtc).ConfigureAwait(false);
            return;
        }

        var nextRetryTime = DateTime.UtcNow.Add(RetryBackoffCalculator.Calculate(newRetryCount));
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
                Logger.Info($"[Retry-Cloud] 已清理 {deleted} 条过期弃置 retry 记录。");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Retry-Cloud] 清理过期弃置记录失败：{ex.Message}");
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

    private CellDataBase? DeserializeCellData(string processType, string json)
    {
        try
        {
            return CellDataJsonSerializer.Deserialize(processType, json);
        }
        catch (Exception ex)
        {
            Logger.Error($"[Retry-Cloud] CellData 反序列化失败：{ex.Message}");
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
        => await DataPipelineDeadLetterWriter.TryPersistAsync(
            _deadLetterStore.SaveAsync,
            _criticalFallbackWriter,
            Logger,
            DeadLetterChannel,
            processType,
            cellDataJson,
            failedTarget,
            sourceTable,
            sourceRecordId,
            stage,
            failureReason).ConfigureAwait(false);

    private enum RetryProcessResult
    {
        Continue,
        Pause
    }
}
