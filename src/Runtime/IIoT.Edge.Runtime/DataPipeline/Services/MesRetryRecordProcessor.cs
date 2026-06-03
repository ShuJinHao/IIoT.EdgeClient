using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

public interface IMesRetryRecordProcessor
{
    Task<MesRetryProcessResult> ProcessAsync(CancellationToken cancellationToken);
}

internal sealed class MesRetryRecordProcessor : IMesRetryRecordProcessor
{
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel = new(
        LogPrefix: "Retry-MES",
        DeadLetterName: "MES",
        CriticalSource: "Retry.MesDeadLetterPersistFailed");

    private const int MaxRetryCount = 20;
    private const int ClaimBatchSize = 5;

    private static readonly DateTime AbandonedRetryTimeUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

    private readonly ILogService _logger;
    private readonly IMesRetryRecordStore _retryStore;
    private readonly IMesDeadLetterStore _deadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly IMesConsumer _mesConsumer;
    private readonly IRetryBackoffStrategy _retryBackoffStrategy;
    private readonly IDataPipelineDeadLetterWriter _deadLetterWriter;
    private readonly IDataPipelineConsumerInvoker _consumerInvoker;
    private readonly ICellDataJsonSerializer _cellDataJsonSerializer;
    private readonly TimeSpan _consumerCallTimeout;

    public MesRetryRecordProcessor(
        ILogService logger,
        IMesRetryRecordStore retryStore,
        IMesDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        IMesConsumer mesConsumer,
        IRetryBackoffStrategy retryBackoffStrategy,
        IDataPipelineDeadLetterWriter deadLetterWriter,
        IDataPipelineConsumerInvoker consumerInvoker,
        ICellDataJsonSerializer cellDataJsonSerializer,
        DataPipelineRuntimeOptions? runtimeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(consumerInvoker);
        ArgumentNullException.ThrowIfNull(cellDataJsonSerializer);

        _logger = logger;
        _retryStore = retryStore;
        _deadLetterStore = deadLetterStore;
        _criticalFallbackWriter = criticalFallbackWriter;
        _mesConsumer = mesConsumer;
        _retryBackoffStrategy = retryBackoffStrategy;
        _deadLetterWriter = deadLetterWriter;
        _consumerInvoker = consumerInvoker;
        _cellDataJsonSerializer = cellDataJsonSerializer;
        _consumerCallTimeout = (runtimeOptions ?? new DataPipelineRuntimeOptions()).GetConsumerCallTimeout();
    }

    public async Task<MesRetryProcessResult> ProcessAsync(CancellationToken cancellationToken)
    {
        var claimedBatch = await _retryStore.ClaimPendingBatchAsync(batchSize: ClaimBatchSize).ConfigureAwait(false);
        if (claimedBatch is null || claimedBatch.Records.Count == 0)
        {
            return MesRetryProcessResult.Continue;
        }

        var hadFailure = false;
        try
        {
            foreach (var record in claimedBatch.Records)
            {
                if (!await ProcessOneAsync(record, cancellationToken).ConfigureAwait(false))
                {
                    hadFailure = true;
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                await _retryStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
            }
            catch (Exception releaseEx)
            {
                _logger.Error($"[Retry-MES] 释放 retry 领取标记 {claimedBatch.ClaimToken} 失败：{releaseEx.Message}");
            }

            _logger.Error($"[Retry-MES] retry 批次执行异常：{ex.Message}");
            return MesRetryProcessResult.Failed;
        }

        return hadFailure
            ? MesRetryProcessResult.Failed
            : MesRetryProcessResult.Continue;
    }

    private async Task<bool> ProcessOneAsync(FailedCellRecord record, CancellationToken cancellationToken)
    {
        var cellData = DeserializeCellData(record.ProcessType, record.CellDataJson);
        if (cellData is null)
        {
            var persisted = await TryPersistDeadLetterAsync(
                record.ProcessType,
                record.CellDataJson,
                record.FailedTarget,
                sourceTable: "failed_mes_records",
                sourceRecordId: record.Id,
                DeadLetterStage.RetryDeserialize,
                $"MES retry 记录反序列化失败，工序：{record.ProcessType}。").ConfigureAwait(false);

            if (persisted)
            {
                await _retryStore.DeleteAsync(record.Id).ConfigureAwait(false);
                return true;
            }

            await HandleRetryFailureAsync(
                record,
                "MES retry 记录反序列化失败，且死信持久化也失败。").ConfigureAwait(false);
            return false;
        }

        var completedRecord = new CellCompletedRecord { CellData = cellData };
        bool success;
        try
        {
            success = await _consumerInvoker
                .ExecuteAsync(
                    ct => _mesConsumer.ProcessAsync(completedRecord, ct),
                    _consumerCallTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await HandleRetryFailureAsync(record, "timeout_exceeded").ConfigureAwait(false);
            return false;
        }

        if (success)
        {
            await _retryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            _logger.Info($"[Retry-MES] {cellData.DisplayLabel} 补传成功，记录已删除。");
            return true;
        }

        await HandleRetryFailureAsync(record, "消费者返回失败。").ConfigureAwait(false);
        return false;
    }

    private async Task HandleRetryFailureAsync(FailedCellRecord record, string errorMessage)
    {
        var newRetryCount = record.RetryCount + 1;

        if (newRetryCount > MaxRetryCount)
        {
            _logger.Warn($"[Retry-MES] {record.ProcessType} 已达到最大补传次数 {MaxRetryCount}，自动补传停止。");
            await _retryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, AbandonedRetryTimeUtc).ConfigureAwait(false);
            return;
        }

        var nextRetryTime = DateTime.UtcNow.Add(_retryBackoffStrategy.Calculate(newRetryCount));
        await _retryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, nextRetryTime).ConfigureAwait(false);
    }

    private CellDataBase? DeserializeCellData(string processType, string json)
    {
        try
        {
            return _cellDataJsonSerializer.Deserialize(processType, json);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Retry-MES] CellData 反序列化失败：{ex.Message}");
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
