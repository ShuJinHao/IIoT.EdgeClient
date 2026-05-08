using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

public interface ICloudRetryRecordProcessor
{
    Task<CloudRetryProcessResult> ProcessAsync(CancellationToken cancellationToken);
}

internal sealed class CloudRetryRecordProcessor : ICloudRetryRecordProcessor
{
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel = new(
        LogPrefix: "Retry-Cloud",
        DeadLetterName: "Cloud",
        CriticalSource: "Retry.CloudDeadLetterPersistFailed");

    private const int MaxRetryCount = 20;
    private const int ClaimBatchSize = 100;
    private const int CloudBatchSize = 100;

    private static readonly DateTime AbandonedRetryTimeUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

    private readonly ILogService _logger;
    private readonly ICloudRetryRecordStore _retryStore;
    private readonly ICloudDeadLetterStore _deadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly ICloudConsumer _cloudConsumer;
    private readonly ICloudBatchConsumer _cloudBatchConsumer;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly IRetryBackoffStrategy _retryBackoffStrategy;
    private readonly IDataPipelineDeadLetterWriter _deadLetterWriter;
    private readonly IDataPipelineConsumerInvoker _consumerInvoker;
    private readonly IProcessIntegrationRegistry? _processIntegrationRegistry;
    private readonly TimeSpan _consumerCallTimeout;

    public CloudRetryRecordProcessor(
        ILogService logger,
        ICloudRetryRecordStore retryStore,
        ICloudDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        ICloudConsumer cloudConsumer,
        ICloudBatchConsumer cloudBatchConsumer,
        ICloudUploadDiagnosticsStore diagnosticsStore,
        IRetryBackoffStrategy retryBackoffStrategy,
        IDataPipelineDeadLetterWriter deadLetterWriter,
        IDataPipelineConsumerInvoker consumerInvoker,
        IProcessIntegrationRegistry? processIntegrationRegistry = null,
        DataPipelineRuntimeOptions? runtimeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(consumerInvoker);

        _logger = logger;
        _retryStore = retryStore;
        _deadLetterStore = deadLetterStore;
        _criticalFallbackWriter = criticalFallbackWriter;
        _cloudConsumer = cloudConsumer;
        _cloudBatchConsumer = cloudBatchConsumer;
        _diagnosticsStore = diagnosticsStore;
        _retryBackoffStrategy = retryBackoffStrategy;
        _deadLetterWriter = deadLetterWriter;
        _consumerInvoker = consumerInvoker;
        _processIntegrationRegistry = processIntegrationRegistry;
        _consumerCallTimeout = (runtimeOptions ?? new DataPipelineRuntimeOptions()).GetConsumerCallTimeout();
    }

    public async Task<CloudRetryProcessResult> ProcessAsync(CancellationToken cancellationToken)
    {
        var claimedBatch = await _retryStore.ClaimPendingBatchAsync(batchSize: ClaimBatchSize).ConfigureAwait(false);
        if (claimedBatch is null || claimedBatch.Records.Count == 0)
        {
            return CloudRetryProcessResult.Continue;
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
                foreach (var chunk in processGroup.Chunk(CloudBatchSize))
                {
                    var processResult = await ProcessBatchChunkAsync(
                        claimedBatch.ClaimToken,
                        processGroup.Key,
                        chunk,
                        cancellationToken).ConfigureAwait(false);

                    if (processResult == CloudRetryProcessResult.PauseForRecovery)
                    {
                        return processResult;
                    }
                }
            }

            foreach (var record in others)
            {
                var processResult = await ProcessOneAsync(record, cancellationToken).ConfigureAwait(false);
                if (processResult == CloudRetryProcessResult.PauseForRecovery)
                {
                    await ReleaseClaimAndPauseAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                    return processResult;
                }
            }

            return CloudRetryProcessResult.Continue;
        }
        catch (Exception ex)
        {
            try
            {
                await _retryStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
            }
            catch (Exception releaseEx)
            {
                _logger.Error($"[Retry-Cloud] 释放 retry 领取标记 {claimedBatch.ClaimToken} 失败：{releaseEx.Message}");
            }

            _logger.Error($"[Retry-Cloud] retry 批次执行异常：{ex.Message}");
            return CloudRetryProcessResult.Failed;
        }
    }

    private async Task<CloudRetryProcessResult> ProcessBatchChunkAsync(
        string claimToken,
        string processType,
        IEnumerable<FailedCellRecord> chunk,
        CancellationToken cancellationToken)
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
            return CloudRetryProcessResult.Continue;
        }

        CloudCallResult result;
        try
        {
            result = await _consumerInvoker
                .ExecuteAsync(
                    ct => _cloudBatchConsumer.ProcessBatchAsync(completedRecords, ct),
                    _consumerCallTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            foreach (var source in validSourceRecords)
            {
                await HandleRetryFailureAsync(source, "timeout_exceeded").ConfigureAwait(false);
            }

            _logger.Warn($"[Retry-Cloud] {processType} 批量补传超时，数量：{validSourceRecords.Count}。");
            return CloudRetryProcessResult.Continue;
        }

        if (result.IsSuccess)
        {
            foreach (var source in validSourceRecords)
            {
                await _retryStore.DeleteAsync(source.Id).ConfigureAwait(false);
            }

            _logger.Info($"[Retry-Cloud] {processType} 批量补传成功，数量：{validSourceRecords.Count}。");
            return CloudRetryProcessResult.Continue;
        }

        if (ShouldPauseForRecovery(result))
        {
            await ReleaseClaimAndPauseAsync(claimToken).ConfigureAwait(false);
            _logger.Warn($"[Retry-Cloud] {processType} 批量补传已暂停，结果：{result.Outcome}，原因：{result.ReasonCode}。");
            return CloudRetryProcessResult.PauseForRecovery;
        }

        foreach (var source in validSourceRecords)
        {
            await HandleRetryFailureAsync(source, $"Cloud 批量补传失败（{result.ReasonCode}）。").ConfigureAwait(false);
        }

        _logger.Warn($"[Retry-Cloud] {processType} 批量补传失败，数量：{validSourceRecords.Count}。");
        return CloudRetryProcessResult.Continue;
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

    private async Task<CloudRetryProcessResult> ProcessOneAsync(
        FailedCellRecord record,
        CancellationToken cancellationToken)
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

            return CloudRetryProcessResult.Continue;
        }

        CloudCallResult result;
        try
        {
            result = await _consumerInvoker
                .ExecuteAsync(
                    ct => _cloudConsumer.ProcessWithResultAsync(new CellCompletedRecord { CellData = cellData }, ct),
                    _consumerCallTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await HandleRetryFailureAsync(record, "timeout_exceeded").ConfigureAwait(false);
            return CloudRetryProcessResult.Continue;
        }

        if (result.IsSuccess)
        {
            await _retryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            _logger.Info($"[Retry-Cloud] {cellData.DisplayLabel} 补传成功，记录已删除。");
            return CloudRetryProcessResult.Continue;
        }

        if (ShouldPauseForRecovery(result))
        {
            _logger.Warn($"[Retry-Cloud] {cellData.DisplayLabel} 补传已暂停，结果：{result.Outcome}，原因：{result.ReasonCode}。");
            return CloudRetryProcessResult.PauseForRecovery;
        }

        await HandleRetryFailureAsync(record, result.ReasonCode).ConfigureAwait(false);
        return CloudRetryProcessResult.Continue;
    }

    private async Task HandleRetryFailureAsync(FailedCellRecord record, string errorMessage)
    {
        var newRetryCount = record.RetryCount + 1;
        _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.Backoff);

        if (newRetryCount > MaxRetryCount)
        {
            _logger.Warn($"[Retry-Cloud] {record.ProcessType} 已达到最大补传次数 {MaxRetryCount}，自动补传停止。");
            await _retryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, AbandonedRetryTimeUtc).ConfigureAwait(false);
            return;
        }

        var nextRetryTime = DateTime.UtcNow.Add(_retryBackoffStrategy.Calculate(newRetryCount));
        await _retryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, nextRetryTime).ConfigureAwait(false);
    }

    private async Task ReleaseClaimAndPauseAsync(string claimToken)
    {
        await _retryStore.ReleaseClaimAsync(claimToken).ConfigureAwait(false);
        _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
    }

    private static bool ShouldPauseForRecovery(CloudCallResult result)
        => result.Outcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry;

    private CellDataBase? DeserializeCellData(string processType, string json)
    {
        try
        {
            return CellDataJsonSerializer.Deserialize(processType, json);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Retry-Cloud] CellData 反序列化失败：{ex.Message}");
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
        => await _deadLetterWriter.TryPersistAsync(
            _deadLetterStore.SaveAsync,
            _criticalFallbackWriter,
            _logger,
            DeadLetterChannel,
            processType,
            cellDataJson,
            failedTarget,
            sourceTable,
            sourceRecordId,
            stage,
            failureReason).ConfigureAwait(false);
}
