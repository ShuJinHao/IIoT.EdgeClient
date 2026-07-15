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
    private readonly TimeSpan _durableShutdownTimeout;
    private readonly TimeProvider _shutdownTimeProvider;
    private readonly int _durableOutletQueueCapacity;
    private readonly Channel<DurableConsumerWorkItem> _cloudDurableQueue;
    private readonly Channel<DurableConsumerWorkItem> _mesDurableQueue;
    private readonly object _workerSync = new();
    private readonly object _durableIdleSync = new();
    private TaskCompletionSource _durableIdleSignal = CreateCompletedIdleSignal();
    private Task? _cloudDurableWorker;
    private Task? _mesDurableWorker;
    private bool _cloudDurableWorkerStopped;
    private bool _mesDurableWorkerStopped;
    private CancellationToken _durableWorkersCancellationToken;
    private bool _durableWorkersTokenInitialized;
    private int _cloudPendingCount;
    private int _mesPendingCount;

    public override string TaskName => "ProcessQueueTask";
    protected override int ExecuteInterval => 0;

    protected override bool ShouldPropagateExecutionFailure(
        Exception exception,
        CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested
           && exception is DurableShutdownPersistenceException;

    public ProcessQueueTask(
        ILogService logger,
        IDataPipelineService pipelineService,
        IEnumerable<ICellDataConsumer> consumers,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        DataPipelineCascadingPersistenceWriter persistenceWriter,
        IDataPipelineConsumerInvoker consumerInvoker,
        DataPipelineRuntimeOptions? runtimeOptions = null,
        TimeProvider? shutdownTimeProvider = null)
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
        _durableShutdownTimeout = options.GetDurableShutdownTimeout();
        _shutdownTimeProvider = shutdownTimeProvider ?? TimeProvider.System;
        _durableOutletQueueCapacity = options.GetDurableOutletQueueCapacity();
        _cloudDurableQueue = CreateDurableQueue(_durableOutletQueueCapacity);
        _mesDurableQueue = CreateDurableQueue(_durableOutletQueueCapacity);
    }

    protected override async Task ExecuteAsync()
    {
        try
        {
            await EnsureDurableWorkersStartedAsync(CurrentCancellationToken).ConfigureAwait(false);

            var drainedCount = 0;
            while (drainedCount < MaxDrainBatchSize
                   && _pipelineService.TryDequeue(out var record)
                   && record is not null)
            {
                await ProcessOneAsync(record, CurrentCancellationToken).ConfigureAwait(false);
                drainedCount++;
            }
        }
        catch (OperationCanceledException) when (CurrentCancellationToken.IsCancellationRequested)
        {
            await WaitForDurableWorkersStoppedAsync().ConfigureAwait(false);
            throw;
        }
    }

    protected override async Task WaitForNextIterationAsync(CancellationToken ct)
    {
        try
        {
            await _pipelineService.WaitToReadAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await WaitForDurableWorkersStoppedAsync().ConfigureAwait(false);
            throw;
        }
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
                await DispatchDurableConsumerAsync(record, consumer, cancellationToken).ConfigureAwait(false);
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
                await HandleFailureAsync(record, consumer, "消费者返回失败。", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DataPipelineNonRetryableException ex)
        {
            var sourceTable = DataPipelineRetryChannelMetadata.TryGetFailedRecordSourceTable(consumer.RetryChannel);
            if (string.IsNullOrWhiteSpace(sourceTable))
            {
                _criticalFallbackWriter.Write(
                    "DataPipeline.ProcessQueue.InvalidNonRetryableChannel",
                    $"工序 {record.CellData.ProcessType} 的永久失败记录无死信通道：{ex.ReasonCode}。");
                return;
            }

            await _persistenceWriter.PersistNonRetryableAsync(
                    record,
                    consumer.RetryChannel,
                    consumer.Name,
                    ex.ReasonCode,
                    sourceTable,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(record, consumer, ResolveFailureMessage(ex), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleFailureAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
                DeadLetterStage.FallbackPersist,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ResolveFailureMessage(Exception ex)
        => ex is TimeoutException ? "处理超时。" : ex.Message;

    protected async Task WaitForDurableQueuesIdleAsync(TimeSpan timeout)
    {
        Task idleTask;
        lock (_durableIdleSync)
        {
            if (_cloudPendingCount == 0 && _mesPendingCount == 0)
            {
                return;
            }

            idleTask = _durableIdleSignal.Task;
        }

        await idleTask.WaitAsync(timeout).ConfigureAwait(false);
    }

    private static TaskCompletionSource CreateCompletedIdleSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private static Channel<DurableConsumerWorkItem> CreateDurableQueue(int capacity)
        => Channel.CreateBounded<DurableConsumerWorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private async Task EnsureDurableWorkersStartedAsync(CancellationToken cancellationToken)
    {
        Task[] previousWorkers;
        lock (_workerSync)
        {
            if (_durableWorkersTokenInitialized &&
                _durableWorkersCancellationToken == cancellationToken)
            {
                StartMissingDurableWorkers(cancellationToken);
                return;
            }

            previousWorkers = new[] { _cloudDurableWorker, _mesDurableWorker }
                .Where(static worker => worker is not null && !worker.IsCompleted)
                .Cast<Task>()
                .ToArray();
            if (previousWorkers.Length == 0)
            {
                ResetAndStartDurableWorkers(cancellationToken);
                return;
            }
        }

        await Task.WhenAll(previousWorkers).WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_workerSync)
        {
            if (_durableWorkersTokenInitialized &&
                _durableWorkersCancellationToken == cancellationToken)
            {
                StartMissingDurableWorkers(cancellationToken);
                return;
            }

            ResetAndStartDurableWorkers(cancellationToken);
        }
    }

    private void ResetAndStartDurableWorkers(CancellationToken cancellationToken)
    {
        _durableWorkersCancellationToken = cancellationToken;
        _durableWorkersTokenInitialized = true;
        _cloudDurableWorker = null;
        _mesDurableWorker = null;
        _cloudDurableWorkerStopped = false;
        _mesDurableWorkerStopped = false;
        StartMissingDurableWorkers(cancellationToken);
    }

    private void StartMissingDurableWorkers(CancellationToken cancellationToken)
    {
        if (_cloudDurableWorker is null || _cloudDurableWorker.IsCompleted)
        {
            _cloudDurableWorkerStopped = false;
            _cloudDurableWorker = Task.Run(
                () => RunDurableWorkerAsync(
                    DataPipelineRetryChannel.Cloud,
                    _cloudDurableQueue.Reader,
                    cancellationToken),
                CancellationToken.None);
        }

        if (_mesDurableWorker is null || _mesDurableWorker.IsCompleted)
        {
            _mesDurableWorkerStopped = false;
            _mesDurableWorker = Task.Run(
                () => RunDurableWorkerAsync(
                    DataPipelineRetryChannel.Mes,
                    _mesDurableQueue.Reader,
                    cancellationToken),
                CancellationToken.None);
        }
    }

    private async Task DispatchDurableConsumerAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writer = ResolveDurableQueueWriter(consumer.RetryChannel);
        if (writer is null)
        {
            await HandleFailureAsync(record, consumer, "关键消费者未配置有效目标出口队列。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var accepted = false;
        var workerStopped = false;
        lock (_workerSync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDurableWorkerStopped(consumer.RetryChannel))
            {
                workerStopped = true;
            }
            else
            {
                IncrementPending(consumer.RetryChannel);
                accepted = writer.TryWrite(new DurableConsumerWorkItem(record, consumer));
                if (!accepted)
                {
                    DecrementPending(consumer.RetryChannel);
                }
            }
        }

        if (accepted)
        {
            return;
        }

        var failureMessage = workerStopped
            ? "目标出口后台任务已停止。"
            : "目标出口队列已满。";
        await HandleFailureAsync(record, consumer, failureMessage, cancellationToken).ConfigureAwait(false);
    }

    private bool IsDurableWorkerStopped(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => _cloudDurableWorkerStopped,
            DataPipelineRetryChannel.Mes => _mesDurableWorkerStopped,
            _ => true
        };

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
        var shutdownFailures = new List<Exception>();
        CancellationTokenSource? shutdownDeadline = null;
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await ProcessConsumerAsync(item.Record, item.Consumer, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    shutdownDeadline ??= CreateShutdownDeadline();
                    if (await PersistShutdownWorkItemAsync(channel, item, shutdownDeadline.Token).ConfigureAwait(false) is { } failure)
                    {
                        shutdownFailures.Add(failure);
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    TryWriteUnexpectedDurableFailure(channel, item, ex);
                }
                finally
                {
                    DecrementPending(channel);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            var queuedItems = new List<DurableConsumerWorkItem>();
            lock (_workerSync)
            {
                switch (channel)
                {
                    case DataPipelineRetryChannel.Cloud:
                        _cloudDurableWorkerStopped = true;
                        break;
                    case DataPipelineRetryChannel.Mes:
                        _mesDurableWorkerStopped = true;
                        break;
                }

                while (reader.TryRead(out var queuedItem))
                {
                    queuedItems.Add(queuedItem);
                }
            }

            if (queuedItems.Count > 0)
            {
                shutdownDeadline ??= CreateShutdownDeadline();
                foreach (var queuedItem in queuedItems)
                {
                    try
                    {
                        if (await PersistShutdownWorkItemAsync(channel, queuedItem, shutdownDeadline.Token).ConfigureAwait(false) is { } failure)
                        {
                            shutdownFailures.Add(failure);
                        }
                    }
                    finally
                    {
                        DecrementPending(channel);
                    }
                }
            }

            shutdownDeadline?.Dispose();
            if (shutdownFailures.Count > 0)
            {
                throw new DurableShutdownPersistenceException(
                    $"{DataPipelineRetryChannelMetadata.Format(channel)} durable shutdown 恢复证据写入失败。",
                    new AggregateException(shutdownFailures));
            }
        }
    }

    private CancellationTokenSource CreateShutdownDeadline()
        => new(_durableShutdownTimeout, _shutdownTimeProvider);

    private async Task<Exception?> PersistShutdownWorkItemAsync(
        DataPipelineRetryChannel channel,
        DurableConsumerWorkItem item,
        CancellationToken shutdownToken)
    {
        const string failureReason = "运行时取消前 durable consumer 未完成，转入 shutdown 级联持久化。";
        var sourceTable = DataPipelineRetryChannelMetadata.TryGetFailedRecordSourceTable(channel);
        if (string.IsNullOrWhiteSpace(sourceTable))
        {
            return new InvalidOperationException(
                $"无法为 {DataPipelineRetryChannelMetadata.Format(channel)} durable shutdown 解析补偿表。");
        }

        try
        {
            await _persistenceWriter.PersistAsync(
                    item.Record,
                    channel,
                    item.Consumer.Name,
                    failureReason,
                    sourceTable,
                    DeadLetterStage.DurableShutdownPersist,
                    cancellationToken: shutdownToken)
                .ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException ex) when (shutdownToken.IsCancellationRequested)
        {
            var timeout = new TimeoutException(
                $"{DataPipelineRetryChannelMetadata.Format(channel)} durable shutdown 级联持久化超过总时限 {_durableShutdownTimeout}。",
                ex);
            return TryWriteShutdownCriticalEvidence(channel, item, failureReason, timeout);
        }
        catch (Exception ex)
        {
            return TryWriteShutdownCriticalEvidence(channel, item, failureReason, ex);
        }
    }

    private Exception? TryWriteShutdownCriticalEvidence(
        DataPipelineRetryChannel channel,
        DurableConsumerWorkItem item,
        string failureReason,
        Exception persistenceFailure)
    {
        try
        {
            _persistenceWriter.WriteDurableShutdownCriticalEvidence(
                item.Record,
                channel,
                item.Consumer.Name,
                failureReason,
                persistenceFailure);
            return null;
        }
        catch (Exception criticalFailure)
        {
            return new AggregateException(
                $"{DataPipelineRetryChannelMetadata.Format(channel)} durable shutdown 无法写入可恢复 critical 证据。",
                persistenceFailure,
                criticalFailure);
        }
    }

    private async Task WaitForDurableWorkersStoppedAsync()
    {
        Task[] workers;
        lock (_workerSync)
        {
            workers = new[] { _cloudDurableWorker, _mesDurableWorker }
                .Where(static worker => worker is not null)
                .Cast<Task>()
                .ToArray();
        }

        if (workers.Length > 0)
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
    }

    private void TryWriteUnexpectedDurableFailure(
        DataPipelineRetryChannel channel,
        DurableConsumerWorkItem item,
        Exception exception)
    {
        var record = item.Record;
        var details =
            $"[PLC-{record.ResolveDeviceName()}][数据管道] 工序={record.CellData.ProcessType} " +
            $"后台出口={DataPipelineRetryChannelMetadata.Format(channel)} 消费异常，" +
            $"模块={record.ModuleId ?? "<unknown>"}，任务={record.TaskKey ?? "<unknown>"}：{exception.Message}";
        try
        {
            _criticalFallbackWriter.Write(
                $"DataPipeline.ProcessQueue.{channel}.UnexpectedConsumerFailure",
                details,
                exception);
        }
        catch (Exception criticalEx)
        {
            Logger.Error($"{details}；critical fallback 写入失败：{criticalEx.Message}");
        }
    }

    private void IncrementPending(DataPipelineRetryChannel channel)
    {
        lock (_durableIdleSync)
        {
            if (_cloudPendingCount == 0 && _mesPendingCount == 0)
            {
                _durableIdleSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            switch (channel)
            {
                case DataPipelineRetryChannel.Cloud:
                    _cloudPendingCount++;
                    break;
                case DataPipelineRetryChannel.Mes:
                    _mesPendingCount++;
                    break;
            }
        }
    }

    private void DecrementPending(DataPipelineRetryChannel channel)
    {
        lock (_durableIdleSync)
        {
            switch (channel)
            {
                case DataPipelineRetryChannel.Cloud:
                    _cloudPendingCount--;
                    break;
                case DataPipelineRetryChannel.Mes:
                    _mesPendingCount--;
                    break;
            }

            if (_cloudPendingCount == 0 && _mesPendingCount == 0)
            {
                _durableIdleSignal.TrySetResult();
            }
        }
    }

    private sealed record DurableConsumerWorkItem(
        CellCompletedRecord Record,
        ICellDataConsumer Consumer);

    private sealed class DurableShutdownPersistenceException(
        string message,
        Exception innerException) : Exception(message, innerException);
}
