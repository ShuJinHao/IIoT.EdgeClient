using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Application.Features.DataPipeline.DeadLetters;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Consumers;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Sdk.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

using IIoT.Edge.Module.Contracts.Cloud;
namespace IIoT.Edge.Host.DataPipeline.Services;

internal sealed class CloudRetryRecordProcessor : RetryRecordProcessorBase<CloudRetryRuntimeState>, ICloudRetryRecordProcessor
{
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel =
        DataPipelineRetryChannelMetadata.CreateDeadLetterChannel(DataPipelineRetryChannel.Cloud);

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
        ICloudRetryDeadLetterTransitionStore retryDeadLetterTransitionStore,
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
            retryDeadLetterTransitionStore.MoveExhaustedRetryToDeadLetterAsync,
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
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var processGroup in batchCandidates.GroupBy(x => x.ProcessType, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var sourceGroup in processGroup.GroupBy(DataPipelineRetryChannelMetadata.CreateSourceKey))
                {
                    foreach (var chunk in sourceGroup.Chunk(CloudBatchSize))
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await TryReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
            foreach (var record in records)
            {
                Logger.Error(
                    $"{DataPipelineLogContext.Format(record)}[云端补传] " +
                    $"结果=BatchFailed，原因码=RetryBatchException，" +
                    $"异常类型={ex.GetType().Name}。");
            }
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
            cancellationToken.ThrowIfCancellationRequested();
            var cellData = DeserializeCellData(source.ProcessType, source.CellDataJson);
            if (cellData is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await HandleDeserializeFailureAsync(
                    source,
                    DataPipelineRetryChannelMetadata.GetFailedRecordSourceTable(DataPipelineRetryChannel.Cloud),
                    $"云端补传记录反序列化失败，工序：{source.ProcessType}。",
                    "云端补传记录反序列化失败，且死信持久化也失败。",
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            completedRecords.Add(DataPipelineRetryChannelMetadata.CreateCompletedRecord(source, cellData));
            var completedRecord = completedRecords[^1];
            var validation = _cloudBatchConsumer.ValidateBatchRecord(completedRecord);
            if (validation.Outcome == CloudCallOutcome.InvalidPayload)
            {
                completedRecords.RemoveAt(completedRecords.Count - 1);
                cancellationToken.ThrowIfCancellationRequested();
                await HandleInvalidPayloadAsync(source, validation.ReasonCode, cancellationToken).ConfigureAwait(false);
                continue;
            }

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
                cancellationToken.ThrowIfCancellationRequested();
                await HandleRetryFailureAsync(source, "处理超时。", cancellationToken).ConfigureAwait(false);
            }

            return CloudRetryProcessResult.Continue;
        }

        if (result.IsSuccess)
        {
            for (var index = 0; index < validSourceRecords.Count; index++)
            {
                var source = validSourceRecords[index];
                cancellationToken.ThrowIfCancellationRequested();
                await RetryStore.DeleteAsync(source.Id).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Logger.Info(
                    $"{DataPipelineLogContext.Format(source, completedRecords[index].CellData)}" +
                    "[云端补传] 结果=Uploaded，本地补传记录已删除。");
            }
            return CloudRetryProcessResult.Continue;
        }

        if (ShouldPauseForRecovery(result))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReleaseClaimAndPauseAsync(claimToken).ConfigureAwait(false);
            for (var index = 0; index < validSourceRecords.Count; index++)
            {
                Logger.Warn(
                    $"{DataPipelineLogContext.Format(validSourceRecords[index], completedRecords[index].CellData)}" +
                    $"[云端补传] 结果=Paused，原因码={result.ReasonCode}。");
            }
            return CloudRetryProcessResult.PauseForRecovery;
        }

        if (result.Outcome == CloudCallOutcome.InvalidPayload)
        {
            foreach (var source in validSourceRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await HandleInvalidPayloadAsync(source, result.ReasonCode, cancellationToken).ConfigureAwait(false);
            }

            return CloudRetryProcessResult.Continue;
        }

        foreach (var source in validSourceRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleRetryFailureAsync(source, $"Cloud 批量补传失败（{result.ReasonCode}）。", cancellationToken).ConfigureAwait(false);
        }

        return CloudRetryProcessResult.Continue;
    }

    private bool IsCloudBatchRetryCandidate(FailedCellRecord record)
        => ResolveUploadMode(record.ProcessType) == ProcessUploadMode.Batch
           && !DataPipelineUploadScenarioResolver.IsDeviceStatus(
               record.TaskKey,
               recordKind: null,
               record.ProcessType);

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
        cancellationToken.ThrowIfCancellationRequested();
        var cellData = DeserializeCellData(record.ProcessType, record.CellDataJson);
        if (cellData is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleDeserializeFailureAsync(
                record,
                DataPipelineRetryChannelMetadata.GetFailedRecordSourceTable(DataPipelineRetryChannel.Cloud),
                $"云端补传记录反序列化失败，工序：{record.ProcessType}。",
                "云端补传记录反序列化失败，且死信持久化也失败。",
                cancellationToken).ConfigureAwait(false);
            return CloudRetryProcessResult.Continue;
        }

        CloudCallResult result;
        try
        {
            result = await _consumerInvoker
                .ExecuteAsync(
                    ct => _cloudConsumer.ProcessWithResultAsync(
                        DataPipelineRetryChannelMetadata.CreateCompletedRecord(record, cellData),
                        ct),
                    _consumerCallTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleRetryFailureAsync(record, "处理超时。", cancellationToken).ConfigureAwait(false);
            return CloudRetryProcessResult.Continue;
        }

        if (result.IsSuccess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RetryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Logger.Info(
                $"{DataPipelineLogContext.Format(record, cellData)}[云端补传] " +
                "结果=Uploaded，本地补传记录已删除。");
            return CloudRetryProcessResult.Continue;
        }

        if (ShouldPauseForRecovery(result))
        {
            Logger.Warn(
                $"{DataPipelineLogContext.Format(record, cellData)}[云端补传] " +
                $"结果=Paused，原因码={result.ReasonCode}。");
            return CloudRetryProcessResult.PauseForRecovery;
        }

        if (result.Outcome == CloudCallOutcome.InvalidPayload)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await HandleInvalidPayloadAsync(record, result.ReasonCode, cancellationToken).ConfigureAwait(false);
            return CloudRetryProcessResult.Continue;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await HandleRetryFailureAsync(record, result.ReasonCode, cancellationToken).ConfigureAwait(false);
        return CloudRetryProcessResult.Continue;
    }

    private async Task HandleInvalidPayloadAsync(
        FailedCellRecord record,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var persisted = await TryPersistDeadLetterAsync(
            record.ProcessType,
            record.CellDataJson,
            record.FailedTarget,
            DataPipelineRetryChannelMetadata.GetFailedRecordSourceTable(DataPipelineRetryChannel.Cloud),
            record.Id,
            DeadLetterStage.InvalidPayload,
            reasonCode,
            record,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (persisted)
        {
            await RetryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        await HandleRetryFailureAsync(
            record,
            $"死信持久化失败：{reasonCode}。",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ReleaseClaimAndPauseAsync(string claimToken)
    {
        await RetryStore.ReleaseClaimAsync(claimToken).ConfigureAwait(false);
        _diagnosticsStore.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
    }

    private async Task TryReleaseClaimAsync(string claimToken)
    {
        try
        {
            await RetryStore.ReleaseClaimAsync(claimToken).ConfigureAwait(false);
        }
        catch (Exception releaseEx)
        {
            Logger.Error(
                $"[云端补传] 结果=ClaimReleaseFailed，" +
                $"异常类型={releaseEx.GetType().Name}。");
        }
    }

    private static bool ShouldPauseForRecovery(CloudCallResult result)
        => result.Outcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry;

}
