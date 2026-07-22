using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

namespace IIoT.Edge.Host.DataPipeline.Services;

internal abstract class RetryDeadLetterServiceBase
{
    private readonly IDeadLetterStore _deadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly IDataPipelineDeadLetterWriter _deadLetterWriter;
    private readonly ICellDataJsonSerializer _cellDataJsonSerializer;

    protected RetryDeadLetterServiceBase(
        ILogService logger,
        IDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        IDataPipelineDeadLetterWriter deadLetterWriter,
        ICellDataJsonSerializer cellDataJsonSerializer,
        DataPipelineDeadLetterChannel deadLetterChannel)
    {
        Logger = logger;
        DeadLetterChannelMetadata = deadLetterChannel;
        _deadLetterStore = deadLetterStore;
        _criticalFallbackWriter = criticalFallbackWriter;
        _deadLetterWriter = deadLetterWriter;
        _cellDataJsonSerializer = cellDataJsonSerializer;
    }

    protected ILogService Logger { get; }

    protected DataPipelineDeadLetterChannel DeadLetterChannelMetadata { get; }

    protected CellDataBase? DeserializeCellData(string processType, string json)
    {
        try
        {
            return _cellDataJsonSerializer.Deserialize(processType, json);
        }
        catch (Exception ex)
        {
            Logger.Error($"[{DeadLetterChannelMetadata.LogPrefix}] CellData 反序列化失败：{ex.Message}");
            return null;
        }
    }

    protected async Task<bool> TryPersistDeadLetterAsync(
        string processType,
        string cellDataJson,
        string failedTarget,
        string sourceTable,
        long sourceRecordId,
        DeadLetterStage stage,
        string failureReason,
        FailedCellRecord? sourceRecord = null,
        CancellationToken cancellationToken = default)
        => await _deadLetterWriter.TryPersistAsync(
            record => _deadLetterStore.SaveAsync(record, cancellationToken),
            _criticalFallbackWriter,
            Logger,
            DeadLetterChannelMetadata,
            processType,
            cellDataJson,
            failedTarget,
            sourceTable,
            sourceRecordId,
            stage,
            failureReason,
            sourceRecord,
            cancellationToken).ConfigureAwait(false);
}
