using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Host.DataPipeline.Services;

internal sealed class MesRetryRecordProcessor : RetryRecordProcessorBase<MesRetryRuntimeState>, IMesRetryRecordProcessor
{
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel =
        DataPipelineRetryChannelMetadata.CreateDeadLetterChannel(DataPipelineRetryChannel.Mes);

    private const int MaxRetryCount = 20;
    private const int ClaimBatchSize = 5;

    private readonly IMesConsumer _mesConsumer;
    private readonly IDataPipelineConsumerInvoker _consumerInvoker;
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
        : base(
            logger,
            retryStore,
            deadLetterStore,
            criticalFallbackWriter,
            retryBackoffStrategy,
            deadLetterWriter,
            cellDataJsonSerializer,
            DeadLetterChannel,
            MaxRetryCount)
    {
        ArgumentNullException.ThrowIfNull(consumerInvoker);
        ArgumentNullException.ThrowIfNull(cellDataJsonSerializer);

        _mesConsumer = mesConsumer;
        _consumerInvoker = consumerInvoker;
        _consumerCallTimeout = (runtimeOptions ?? new DataPipelineRuntimeOptions()).GetConsumerCallTimeout();
    }

    public async Task<MesRetryProcessResult> ProcessAsync(CancellationToken cancellationToken)
    {
        var claimedBatch = await RetryStore.ClaimPendingBatchAsync(batchSize: ClaimBatchSize).ConfigureAwait(false);
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
                await RetryStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
            }
            catch (Exception releaseEx)
            {
                Logger.Error($"[MES补传] 释放补传领取标记 {claimedBatch.ClaimToken} 失败：{releaseEx.Message}");
            }

            Logger.Error($"[MES补传] 补传批次执行异常：{ex.Message}");
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
            return await HandleDeserializeFailureAsync(
                record,
                DataPipelineRetryChannelMetadata.GetFailedRecordSourceTable(DataPipelineRetryChannel.Mes),
                $"MES 补传记录反序列化失败，工序：{record.ProcessType}。",
                "MES 补传记录反序列化失败，且死信持久化也失败。").ConfigureAwait(false);
        }

        var completedRecord = DataPipelineRetryChannelMetadata.CreateCompletedRecord(record, cellData);
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
            await HandleRetryFailureAsync(record, "处理超时。").ConfigureAwait(false);
            return false;
        }

        if (success)
        {
            await RetryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            Logger.Info($"[PLC-{record.DeviceName}][MES补传] {cellData.DisplayLabel} 补传成功，记录已删除。");
            return true;
        }

        await HandleRetryFailureAsync(record, "消费者返回失败。").ConfigureAwait(false);
        return false;
    }

}
