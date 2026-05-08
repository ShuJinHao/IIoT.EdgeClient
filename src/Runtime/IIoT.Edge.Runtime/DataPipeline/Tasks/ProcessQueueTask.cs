using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Runtime.DataPipeline.Tasks;

public class ProcessQueueTask : ScheduledTaskBase
{
    private const int MaxDrainBatchSize = 100;

    private readonly IDataPipelineService _pipelineService;
    private readonly List<ICellDataConsumer> _consumers;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly DataPipelineCascadingPersistenceWriter _persistenceWriter;
    private readonly IDataPipelineConsumerInvoker _consumerInvoker;
    private readonly TimeSpan _consumerCallTimeout;

    public override string TaskName => "ProcessQueueTask";
    protected override int ExecuteInterval => 0;

    public ProcessQueueTask(
        ILogService logger,
        IDataPipelineService pipelineService,
        IEnumerable<ICellDataConsumer> consumers,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        DataPipelineCascadingPersistenceWriter persistenceWriter,
        IDataPipelineConsumerInvoker consumerInvoker,
        DataPipelineRuntimeOptions? runtimeOptions = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(persistenceWriter);
        ArgumentNullException.ThrowIfNull(consumerInvoker);

        _pipelineService = pipelineService;
        _criticalFallbackWriter = criticalFallbackWriter;
        _consumers = consumers.OrderBy(c => c.Order).ToList();
        _persistenceWriter = persistenceWriter;
        _consumerInvoker = consumerInvoker;
        _consumerCallTimeout = (runtimeOptions ?? new DataPipelineRuntimeOptions()).GetConsumerCallTimeout();
    }

    protected override async Task ExecuteAsync()
    {
        var drainedCount = 0;
        while (drainedCount < MaxDrainBatchSize
               && _pipelineService.TryDequeue(out var record)
               && record is not null)
        {
            await ProcessOneAsync(record, CurrentCancellationToken).ConfigureAwait(false);
            drainedCount++;
        }
    }

    protected override async Task WaitForNextIterationAsync(CancellationToken ct)
    {
        await _pipelineService.WaitToReadAsync(ct).ConfigureAwait(false);
    }

    private async Task ProcessOneAsync(CellCompletedRecord record, CancellationToken cancellationToken)
    {
        var label = record.CellData.DisplayLabel;
        Logger.Info($"[{record.CellData.ProcessType}] 开始处理 {label}。");

        foreach (var consumer in _consumers)
        {
            try
            {
                var success = await _consumerInvoker
                    .ExecuteAsync(
                        ct => consumer.ProcessAsync(record, ct),
                        _consumerCallTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!success)
                {
                    await HandleFailureAsync(record, consumer, "consumer_returned_false").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(record, consumer, ResolveFailureMessage(ex)).ConfigureAwait(false);
            }
        }

        Logger.Info($"[{record.CellData.ProcessType}] {label} 处理链路已完成。");
    }

    private async Task HandleFailureAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer,
        string errorMessage)
    {
        var label = record.CellData.DisplayLabel;

        if (consumer.FailureMode == ConsumerFailureMode.BestEffort)
        {
            Logger.Warn($"[{record.CellData.ProcessType}] {consumer.Name} 处理 {label} 失败：{errorMessage}（非关键消费者，继续后续链路）。");
            return;
        }

        if (consumer.RetryChannel == DataPipelineRetryChannel.None)
        {
            var details =
                $"[{record.CellData.ProcessType}] 关键消费者 {consumer.Name} 处理 {label} 失败，但未配置 RetryChannel。";
            Logger.Error(details);
            _criticalFallbackWriter.Write("DataPipeline.ProcessQueue.InvalidRetryChannel", details);
            return;
        }

        Logger.Warn(
            $"[{record.CellData.ProcessType}] {consumer.Name} 处理 {label} 失败，准备写入 {consumer.RetryChannel} 补偿链路。");

        var sourceTable = consumer.RetryChannel switch
        {
            DataPipelineRetryChannel.Cloud => "failed_cloud_records",
            DataPipelineRetryChannel.Mes => "failed_mes_records",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(sourceTable))
        {
            var unsupportedDetails =
                $"[{record.CellData.ProcessType}] {consumer.Name} 使用了不支持的补偿链路：{consumer.RetryChannel}。";
            Logger.Error(unsupportedDetails);
            _criticalFallbackWriter.Write("DataPipeline.ProcessQueue.UnsupportedRetryChannel", unsupportedDetails);
            return;
        }

        await _persistenceWriter.PersistAsync(
                record,
                consumer.RetryChannel,
                consumer.Name,
                errorMessage,
                sourceTable,
                DeadLetterStage.FallbackPersist)
            .ConfigureAwait(false);
    }

    private static string ResolveFailureMessage(Exception ex)
        => ex is TimeoutException ? "timeout_exceeded" : ex.Message;
}
