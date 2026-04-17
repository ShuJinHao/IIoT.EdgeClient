using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Runtime.DataPipeline.Tasks;

public class ProcessQueueTask : ScheduledTaskBase
{
    private readonly IDataPipelineService _pipelineService;
    private readonly List<ICellDataConsumer> _consumers;
    private readonly IFailedRecordStore _failedStore;
    private readonly ICloudFallbackBufferStore _cloudFallbackStore;
    private readonly IMesFallbackBufferStore _mesFallbackStore;

    public override string TaskName => "ProcessQueueTask";
    protected override int ExecuteInterval => 50;

    public ProcessQueueTask(
        ILogService logger,
        IDataPipelineService pipelineService,
        IEnumerable<ICellDataConsumer> consumers,
        IFailedRecordStore failedStore,
        ICloudFallbackBufferStore cloudFallbackStore,
        IMesFallbackBufferStore mesFallbackStore)
        : base(logger)
    {
        _pipelineService = pipelineService;
        _failedStore = failedStore;
        _cloudFallbackStore = cloudFallbackStore;
        _mesFallbackStore = mesFallbackStore;
        _consumers = consumers.OrderBy(c => c.Order).ToList();
    }

    protected override async Task ExecuteAsync()
    {
        if (!_pipelineService.TryDequeue(out var record) || record is null)
        {
            return;
        }

        var label = record.CellData.DisplayLabel;
        Logger.Info($"[{record.CellData.ProcessType}] Start processing {label}");

        foreach (var consumer in _consumers)
        {
            try
            {
                var success = await consumer.ProcessAsync(record).ConfigureAwait(false);
                if (!success)
                {
                    await HandleFailureAsync(record, consumer, "Consumer returned false.");
                }
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(record, consumer, ex.Message);
            }
        }

        Logger.Info($"[{record.CellData.ProcessType}] {label} processing chain completed.");
    }

    private async Task HandleFailureAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer,
        string errorMessage)
    {
        var label = record.CellData.DisplayLabel;

        if (consumer.FailureMode == ConsumerFailureMode.BestEffort)
        {
            Logger.Warn($"[{record.CellData.ProcessType}] {consumer.Name} failed for {label}: {errorMessage} (best-effort)");
            return;
        }

        if (string.IsNullOrWhiteSpace(consumer.RetryChannel))
        {
            Logger.Error($"[{record.CellData.ProcessType}] Durable consumer {consumer.Name} failed for {label}, but RetryChannel is not configured.");
            return;
        }

        Logger.Warn(
            $"[{record.CellData.ProcessType}] {consumer.Name} failed for {label}. Move to retry channel {consumer.RetryChannel}.");

        try
        {
            await _failedStore.SaveAsync(record, consumer.Name, errorMessage, consumer.RetryChannel);
        }
        catch (Exception ex)
        {
            Logger.Error($"[{record.CellData.ProcessType}] Save retry record failed for {label}: {ex.Message}");

            if (string.Equals(consumer.RetryChannel, "Cloud", StringComparison.OrdinalIgnoreCase))
            {
                await TryPersistCloudFallbackAsync(record, consumer.Name, errorMessage);
                return;
            }

            if (string.Equals(consumer.RetryChannel, "MES", StringComparison.OrdinalIgnoreCase))
            {
                await TryPersistMesFallbackAsync(record, consumer.Name, errorMessage);
            }
        }
    }

    private async Task TryPersistCloudFallbackAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage)
    {
        var label = record.CellData.DisplayLabel;

        try
        {
            await _cloudFallbackStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
            Logger.Error($"[{record.CellData.ProcessType}] Main retry store unavailable. Persisted {label} to Cloud fallback buffer.");
        }
        catch (Exception fallbackEx)
        {
            Logger.Fatal($"[{record.CellData.ProcessType}] Cloud fallback buffer also failed for {label}: {fallbackEx.Message}");
        }
    }

    private async Task TryPersistMesFallbackAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage)
    {
        var label = record.CellData.DisplayLabel;

        try
        {
            await _mesFallbackStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
            Logger.Error($"[{record.CellData.ProcessType}] Main retry store unavailable. Persisted {label} to MES fallback buffer.");
        }
        catch (Exception fallbackEx)
        {
            Logger.Fatal($"[{record.CellData.ProcessType}] MES fallback buffer also failed for {label}: {fallbackEx.Message}");
        }
    }
}
