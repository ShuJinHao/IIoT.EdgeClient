using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

using IIoT.Edge.Application.Abstractions.Cloud;
namespace IIoT.Edge.Host.DataPipeline.Services;

internal sealed class CloudRetryRecordProcessor : RetryRecordProcessorBase<CloudRetryRuntimeState>, ICloudRetryRecordProcessor
{
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel = new(
        LogPrefix: "Retry-Cloud",
        DeadLetterName: "Cloud",
        CriticalSource: "Retry.CloudDeadLetterPersistFailed");

    private const int MaxRetryCount = 20;
    private const int ClaimBatchSize = 100;
    private const int CloudBatchSize = 100;

    private readonly ICloudConsumer _cloudConsumer;
    private readonly ICloudBatchConsumer _cloudBatchConsumer;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
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
        ICellDataJsonSerializer cellDataJsonSerializer,
        IProcessIntegrationRegistry? processIntegrationRegistry = null,
        DataPipelineRuntimeOptions? runtimeOptions = null)
        : base(
            logger,
            retryStore,
            deadLetterStore,
            criticalFallbackWriter,
            retryBackoffStrategy,
            deadLetterWriter,
            cellDataJsonSerializer,
            DeadLetterChannel,
            MaxRetryCount,
            diagnosticsStore,
            CloudRetryRuntimeState.Backoff)
    {
        ArgumentNullException.ThrowIfNull(consumerInvoker);

        _cloudConsumer = cloudConsumer;
        _cloudBatchConsumer = cloudBatchConsumer;
        _diagnosticsStore = diagnosticsStore;
        _consumerInvoker = consumerInvoker;
        _processIntegrationRegistry = processIntegrationRegistry;
        _consumerCallTimeout = (runtimeOptions ?? new DataPipelineRuntimeOptions()).GetConsumerCallTimeout();
    }

    public async Task<CloudRetryProcessResult> ProcessAsync(CancellationToken cancellationToken)
    {
        var claimedBatch = await RetryStore.ClaimPendingBatchAsync(batchSize: ClaimBatchSize).ConfigureAwait(false);
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
                await RetryStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
            }
            catch (Exception releaseEx)
            {
            Logger.Error($"[云端补传] 释放补传领取标记 {claimedBatch.ClaimToken} 失败：{releaseEx.Message}");
            }

            Logger.Error($"[云端补传] 补传批次执行异常：{ex.Message}");
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
                await HandleDeserializeFailureAsync(
                    source,
                    "failed_cloud_records",
                    $"云端补传记录反序列化失败，工序：{source.ProcessType}。",
                    "云端补传记录反序列化失败，且死信持久化也失败。").ConfigureAwait(false);
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
                await HandleRetryFailureAsync(source, "处理超时。").ConfigureAwait(false);
            }

            Logger.Warn($"[云端补传] {processType} 批量补传超时，数量：{validSourceRecords.Count}。");
            return CloudRetryProcessResult.Continue;
        }

        if (result.IsSuccess)
        {
            foreach (var source in validSourceRecords)
            {
                await RetryStore.DeleteAsync(source.Id).ConfigureAwait(false);
            }

            Logger.Info($"[云端补传] {processType} 批量补传成功，数量：{validSourceRecords.Count}。");
            return CloudRetryProcessResult.Continue;
        }

        if (ShouldPauseForRecovery(result))
        {
            await ReleaseClaimAndPauseAsync(claimToken).ConfigureAwait(false);
            Logger.Warn($"[云端补传] {processType} 批量补传已暂停，结果：{result.Outcome}，原因：{result.ReasonCode}。");
            return CloudRetryProcessResult.PauseForRecovery;
        }

        foreach (var source in validSourceRecords)
        {
            await HandleRetryFailureAsync(source, $"Cloud 批量补传失败（{result.ReasonCode}）。").ConfigureAwait(false);
        }

        Logger.Warn($"[云端补传] {processType} 批量补传失败，数量：{validSourceRecords.Count}。");
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
            await HandleDeserializeFailureAsync(
                record,
                "failed_cloud_records",
                $"云端补传记录反序列化失败，工序：{record.ProcessType}。",
                "云端补传记录反序列化失败，且死信持久化也失败。").ConfigureAwait(false);
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
            await HandleRetryFailureAsync(record, "处理超时。").ConfigureAwait(false);
            return CloudRetryProcessResult.Continue;
        }

        if (result.IsSuccess)
        {
            await RetryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            Logger.Info($"[云端补传] {cellData.DisplayLabel} 补传成功，记录已删除。");
            return CloudRetryProcessResult.Continue;
        }

        if (ShouldPauseForRecovery(result))
        {
            Logger.Warn($"[云端补传] {cellData.DisplayLabel} 补传已暂停，结果：{result.Outcome}，原因：{result.ReasonCode}。");
            return CloudRetryProcessResult.PauseForRecovery;
        }

        await HandleRetryFailureAsync(record, result.ReasonCode).ConfigureAwait(false);
        return CloudRetryProcessResult.Continue;
    }

    private async Task ReleaseClaimAndPauseAsync(string claimToken)
    {
        await RetryStore.ReleaseClaimAsync(claimToken).ConfigureAwait(false);
        _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
    }

    private static bool ShouldPauseForRecovery(CloudCallResult result)
        => result.Outcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry;
}
