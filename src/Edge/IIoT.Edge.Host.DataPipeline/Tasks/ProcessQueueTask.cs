using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using System.Threading.Channels;

namespace IIoT.Edge.Host.DataPipeline.Tasks;

public class ProcessQueueTask : ScheduledTaskBase
{
    private const int MaxDrainBatchSize = 100;

    private readonly IDataPipelineService _pipelineService;
    private readonly List<ICellDataConsumer> _consumers;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly DataPipelineCascadingPersistenceWriter _persistenceWriter;
    private readonly IDataPipelineConsumerInvoker _consumerInvoker;
    private readonly TimeSpan _consumerCallTimeout;
    private readonly int _durableOutletQueueCapacity;
    private readonly Channel<DurableConsumerWorkItem> _cloudDurableQueue;
    private readonly Channel<DurableConsumerWorkItem> _mesDurableQueue;
    private readonly object _workerSync = new();
    private Task? _cloudDurableWorker;
    private Task? _mesDurableWorker;
    private int _cloudPendingCount;
    private int _mesPendingCount;

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
        var options = runtimeOptions ?? new DataPipelineRuntimeOptions();
        _consumerCallTimeout = options.GetConsumerCallTimeout();
        _durableOutletQueueCapacity = options.GetDurableOutletQueueCapacity();
        _cloudDurableQueue = CreateDurableQueue(_durableOutletQueueCapacity);
        _mesDurableQueue = CreateDurableQueue(_durableOutletQueueCapacity);
    }

    protected override async Task ExecuteAsync()
    {
        EnsureDurableWorkersStarted(CurrentCancellationToken);

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
        var deviceName = record.ResolveDeviceName();
        Logger.Info($"[PLC-{deviceName}][数据管道] 工序={record.CellData.ProcessType} 开始处理 {label}。");

        foreach (var consumer in _consumers.Where(consumer => DataPipelineRetryChannelMetadata.ShouldProcess(record, consumer)))
        {
            if (consumer.FailureMode == ConsumerFailureMode.Durable)
            {
                await DispatchDurableConsumerAsync(record, consumer).ConfigureAwait(false);
                continue;
            }

            await ProcessConsumerAsync(record, consumer, cancellationToken).ConfigureAwait(false);
        }

        Logger.Info($"[PLC-{deviceName}][数据管道] 工序={record.CellData.ProcessType} {label} 已完成本地处理并投递目标出口。");
    }

    private async Task ProcessConsumerAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer,
        CancellationToken cancellationToken)
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
                await HandleFailureAsync(record, consumer, "消费者返回失败。").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(record, consumer, ResolveFailureMessage(ex)).ConfigureAwait(false);
        }
    }

    private async Task HandleFailureAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer,
        string errorMessage)
    {
        var label = record.CellData.DisplayLabel;
        var deviceName = record.ResolveDeviceName();

        if (consumer.FailureMode == ConsumerFailureMode.BestEffort)
        {
            Logger.Warn($"[PLC-{deviceName}][数据管道] 工序={record.CellData.ProcessType} {consumer.Name} 处理 {label} 失败：{errorMessage}（非关键消费者，继续后续链路）。");
            return;
        }

        if (consumer.RetryChannel == DataPipelineRetryChannel.None)
        {
            var details =
                $"[PLC-{deviceName}][数据管道] 工序={record.CellData.ProcessType} 关键消费者 {consumer.Name} 处理 {label} 失败，但未配置补偿链路。";
            Logger.Error(details);
            _criticalFallbackWriter.Write("DataPipeline.ProcessQueue.InvalidRetryChannel", details);
            return;
        }

        Logger.Warn(
            $"[PLC-{deviceName}][数据管道] 工序={record.CellData.ProcessType} {consumer.Name} 处理 {label} 失败，准备写入 {DataPipelineRetryChannelMetadata.Format(consumer.RetryChannel)} 补偿链路。");

        var sourceTable = DataPipelineRetryChannelMetadata.TryGetFailedRecordSourceTable(consumer.RetryChannel);

        if (string.IsNullOrWhiteSpace(sourceTable))
        {
            var unsupportedDetails =
                $"[PLC-{deviceName}][数据管道] 工序={record.CellData.ProcessType} {consumer.Name} 使用了不支持的补偿链路：{DataPipelineRetryChannelMetadata.Format(consumer.RetryChannel)}。";
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
        => ex is TimeoutException ? "处理超时。" : ex.Message;

    protected async Task WaitForDurableQueuesIdleAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (Volatile.Read(ref _cloudPendingCount) > 0
               || Volatile.Read(ref _mesPendingCount) > 0)
        {
            await Task.Delay(10, cts.Token).ConfigureAwait(false);
        }
    }

    private static Channel<DurableConsumerWorkItem> CreateDurableQueue(int capacity)
        => Channel.CreateBounded<DurableConsumerWorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private void EnsureDurableWorkersStarted(CancellationToken cancellationToken)
    {
        lock (_workerSync)
        {
            _cloudDurableWorker ??= Task.Run(
                () => RunDurableWorkerAsync(
                    DataPipelineRetryChannel.Cloud,
                    _cloudDurableQueue.Reader,
                    cancellationToken),
                CancellationToken.None);
            _mesDurableWorker ??= Task.Run(
                () => RunDurableWorkerAsync(
                    DataPipelineRetryChannel.Mes,
                    _mesDurableQueue.Reader,
                    cancellationToken),
                CancellationToken.None);
        }
    }

    private async Task DispatchDurableConsumerAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer)
    {
        var writer = ResolveDurableQueueWriter(consumer.RetryChannel);
        if (writer is null)
        {
            await HandleFailureAsync(record, consumer, "关键消费者未配置有效目标出口队列。").ConfigureAwait(false);
            return;
        }

        IncrementPending(consumer.RetryChannel);
        if (writer.TryWrite(new DurableConsumerWorkItem(record, consumer)))
        {
            return;
        }

        DecrementPending(consumer.RetryChannel);
        await HandleFailureAsync(record, consumer, "目标出口队列已满。").ConfigureAwait(false);
    }

    private ChannelWriter<DurableConsumerWorkItem>? ResolveDurableQueueWriter(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => _cloudDurableQueue.Writer,
            DataPipelineRetryChannel.Mes => _mesDurableQueue.Writer,
            _ => null
        };

    private async Task RunDurableWorkerAsync(
        DataPipelineRetryChannel channel,
        ChannelReader<DurableConsumerWorkItem> reader,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await ProcessConsumerAsync(item.Record, item.Consumer, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    DecrementPending(channel);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void IncrementPending(DataPipelineRetryChannel channel)
    {
        switch (channel)
        {
            case DataPipelineRetryChannel.Cloud:
                Interlocked.Increment(ref _cloudPendingCount);
                break;
            case DataPipelineRetryChannel.Mes:
                Interlocked.Increment(ref _mesPendingCount);
                break;
        }
    }

    private void DecrementPending(DataPipelineRetryChannel channel)
    {
        switch (channel)
        {
            case DataPipelineRetryChannel.Cloud:
                Interlocked.Decrement(ref _cloudPendingCount);
                break;
            case DataPipelineRetryChannel.Mes:
                Interlocked.Decrement(ref _mesPendingCount);
                break;
        }
    }

    private sealed record DurableConsumerWorkItem(
        CellCompletedRecord Record,
        ICellDataConsumer Consumer);
}
