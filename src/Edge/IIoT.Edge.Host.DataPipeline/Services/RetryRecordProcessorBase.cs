using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Host.DataPipeline.Services;

internal abstract class RetryRecordProcessorBase<TRuntimeState> : RetryDeadLetterServiceBase
    where TRuntimeState : struct, Enum
{
    private readonly IRetryBackoffStrategy _retryBackoffStrategy;
    private readonly IRetryDiagnosticsStore<TRuntimeState>? _diagnosticsStore;
    private readonly TRuntimeState _backoffState;
    private readonly int _maxRetryCount;
    private readonly Func<FailedCellRecord, int, string, CancellationToken, Task>
        _moveExhaustedRetryToDeadLetterAsync;

    protected RetryRecordProcessorBase(
        ILogService logger,
        IRetryRecordStore retryStore,
        IDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        IRetryBackoffStrategy retryBackoffStrategy,
        IDataPipelineDeadLetterWriter deadLetterWriter,
        ICellDataJsonSerializer cellDataJsonSerializer,
        DataPipelineDeadLetterChannel deadLetterChannel,
        Func<FailedCellRecord, int, string, CancellationToken, Task> moveExhaustedRetryToDeadLetterAsync,
        int maxRetryCount,
        IRetryDiagnosticsStore<TRuntimeState>? diagnosticsStore = null,
        TRuntimeState backoffState = default)
        : base(
            logger,
            deadLetterStore,
            criticalFallbackWriter,
            deadLetterWriter,
            cellDataJsonSerializer,
            deadLetterChannel)
    {
        RetryStore = retryStore;
        _retryBackoffStrategy = retryBackoffStrategy;
        _diagnosticsStore = diagnosticsStore;
        _backoffState = backoffState;
        _maxRetryCount = maxRetryCount;
        _moveExhaustedRetryToDeadLetterAsync = moveExhaustedRetryToDeadLetterAsync;
    }

    protected IRetryRecordStore RetryStore { get; }

    protected async Task<bool> HandleDeserializeFailureAsync(
        FailedCellRecord record,
        string sourceTable,
        string deadLetterFailureReason,
        string retryFailureReason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var persisted = await TryPersistDeadLetterAsync(
            record.ProcessType,
            record.CellDataJson,
            record.FailedTarget,
            sourceTable,
            record.Id,
            DeadLetterStage.RetryDeserialize,
            deadLetterFailureReason,
            record,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (persisted)
        {
            await RetryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }

        await HandleRetryFailureAsync(record, retryFailureReason, cancellationToken).ConfigureAwait(false);
        return false;
    }

    protected async Task HandleRetryFailureAsync(
        FailedCellRecord record,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var newRetryCount = record.RetryCount + 1;
        _diagnosticsStore?.SetRuntimeState(_backoffState);

        if (newRetryCount >= _maxRetryCount)
        {
            await _moveExhaustedRetryToDeadLetterAsync(
                    record,
                    newRetryCount,
                    errorMessage,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Logger.Warn(
                $"{DataPipelineLogContext.Format(record)}" +
                $"[{DeadLetterChannelMetadata.LogPrefix}] 工序={record.ProcessType}，记录={record.Id}，" +
                $"结果=DurableDeadLetter，重试次数={newRetryCount}，尚未上传成功。");
            return;
        }

        var nextRetryTime = DateTime.UtcNow.Add(_retryBackoffStrategy.Calculate(newRetryCount));
        await RetryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, nextRetryTime).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Logger.Warn(
            $"{DataPipelineLogContext.Format(record)}" +
            $"[{DeadLetterChannelMetadata.LogPrefix}] 结果=RetryScheduled，" +
            $"重试次数={newRetryCount}，下次时间Utc={nextRetryTime:O}。");
    }
}
