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
    private static readonly DateTime AbandonedRetryTimeUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

    private readonly IRetryBackoffStrategy _retryBackoffStrategy;
    private readonly IRetryDiagnosticsStore<TRuntimeState>? _diagnosticsStore;
    private readonly TRuntimeState _backoffState;
    private readonly int _maxRetryCount;

    protected RetryRecordProcessorBase(
        ILogService logger,
        IRetryRecordStore retryStore,
        IDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        IRetryBackoffStrategy retryBackoffStrategy,
        IDataPipelineDeadLetterWriter deadLetterWriter,
        ICellDataJsonSerializer cellDataJsonSerializer,
        DataPipelineDeadLetterChannel deadLetterChannel,
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

        if (newRetryCount > _maxRetryCount)
        {
            Logger.Warn($"[PLC-{record.DeviceName}][{DeadLetterChannelMetadata.LogPrefix}] {record.ProcessType} 已达到最大补传次数 {_maxRetryCount}，自动补传停止。");
            await RetryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, AbandonedRetryTimeUtc).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        var nextRetryTime = DateTime.UtcNow.Add(_retryBackoffStrategy.Calculate(newRetryCount));
        await RetryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, nextRetryTime).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
