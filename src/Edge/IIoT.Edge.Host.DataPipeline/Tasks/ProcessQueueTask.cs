using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace IIoT.Edge.Host.DataPipeline.Tasks;

public class ProcessQueueTask : ScheduledTaskBase
{
    private const int MaxDrainBatchSize = 100;

    private readonly IDataPipelineService _pipelineService;
    private readonly IDataPipelineIngressStore? _ingressStore;
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
    private readonly ConcurrentDictionary<string, byte> _inflightIngressConsumers =
        new(StringComparer.OrdinalIgnoreCase);

    public override string TaskName => "ProcessQueueTask";
    protected override int ExecuteInterval => 0;

    protected override bool ShouldPropagateExecutionFailure(
        Exception exception,
        CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested
           && exception is DurableShutdownPersistenceException;

    protected override Task OnStoppingAsync()
        => WaitForDurableWorkersStoppedAsync();

    public ProcessQueueTask(
        ILogService logger,
        IDataPipelineService pipelineService,
        IEnumerable<ICellDataConsumer> consumers,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        DataPipelineCascadingPersistenceWriter persistenceWriter,
        IDataPipelineConsumerInvoker consumerInvoker,
        DataPipelineRuntimeOptions? runtimeOptions = null,
        TimeProvider? shutdownTimeProvider = null,
        IDataPipelineIngressStore? ingressStore = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(persistenceWriter);
        ArgumentNullException.ThrowIfNull(consumerInvoker);

        _pipelineService = pipelineService;
        _ingressStore = ingressStore;
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
        await EnsureDurableWorkersStartedAsync(CurrentCancellationToken).ConfigureAwait(false);

        if (_ingressStore is not null)
        {
            // 内存队列只作唤醒通知；业务真值始终从持久入口按接受顺序取回。
            var notificationCount = 0;
            while (notificationCount < MaxDrainBatchSize
                   && _pipelineService.TryDequeue(out _))
            {
                notificationCount++;
            }

            var pending = await _ingressStore
                .GetPendingAsync(MaxDrainBatchSize, CurrentCancellationToken)
                .ConfigureAwait(false);
            foreach (var ingress in pending)
            {
                await ProcessIngressAsync(ingress, CurrentCancellationToken).ConfigureAwait(false);
            }

            return;
        }

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
        if (_ingressStore is null)
        {
            await _pipelineService.WaitToReadAsync(ct).ConfigureAwait(false);
            return;
        }

        var queueReady = _pipelineService.WaitToReadAsync(ct).AsTask();
        var durableRecoveryPoll = Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        await Task.WhenAny(queueReady, durableRecoveryPoll).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    private async Task ProcessOneAsync(CellCompletedRecord record, CancellationToken cancellationToken)
    {
        WriteLogBestEffort(() =>
            Logger.Info(
                $"{DataPipelineLogContext.Format(record)}[数据管道] 结果=ProcessingStarted。"));

        foreach (var consumer in _consumers.Where(consumer => DataPipelineRetryChannelMetadata.ShouldProcess(record, consumer)))
        {
            if (consumer.FailureMode == ConsumerFailureMode.Durable)
            {
                await DispatchDurableConsumerAsync(record, consumer, cancellationToken, ingress: null).ConfigureAwait(false);
                continue;
            }

            await ProcessConsumerAsync(record, consumer, cancellationToken).ConfigureAwait(false);
        }

        WriteLogBestEffort(() =>
            Logger.Info(
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                "结果=ConsumersDispatched，尚不代表外部上传成功。"));
    }

    private async Task ProcessIngressAsync(
        DataPipelineIngressRecord ingress,
        CancellationToken cancellationToken)
    {
        var record = ingress.Record;
        var applicableConsumers = _consumers
            .Where(consumer => DataPipelineRetryChannelMetadata.ShouldProcess(record, consumer))
            .ToArray();
        var allConsumerKeys = applicableConsumers
            .Select(CreateConsumerKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requiredDurableConsumerKeys = applicableConsumers
            .Where(static consumer => consumer.FailureMode == ConsumerFailureMode.Durable)
            .Select(CreateConsumerKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allConsumerKeys.Length != applicableConsumers.Length)
        {
            throw new InvalidOperationException("数据管道消费者稳定键重复，禁止消费不可区分的回执。");
        }

        WriteLogBestEffort(() => Logger.Info(
            $"{DataPipelineLogContext.Format(record)}[数据管道] 结果=RecoveryStarted。"));

        foreach (var consumer in applicableConsumers)
        {
            var consumerKey = CreateConsumerKey(consumer);
            if (ingress.CompletedConsumerKeys.Contains(consumerKey))
            {
                continue;
            }

            var context = new IngressConsumerContext(
                ingress.CompletionId,
                consumerKey,
                requiredDurableConsumerKeys);
            if (consumer.FailureMode == ConsumerFailureMode.Durable)
            {
                await DispatchDurableConsumerAsync(record, consumer, cancellationToken, context)
                    .ConfigureAwait(false);
                continue;
            }

            if (await ProcessConsumerAsync(record, consumer, cancellationToken).ConfigureAwait(false))
            {
                await MarkIngressConsumerCompletedAsync(context, record, cancellationToken).ConfigureAwait(false);
            }
        }

        var completed = await _ingressStore!
            .CompleteIfAllConsumersFinishedAsync(
                ingress.CompletionId,
                requiredDurableConsumerKeys,
                cancellationToken)
            .ConfigureAwait(false);
        if (completed)
        {
            WriteLogBestEffort(() => Logger.Info(
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                "结果=IngressCompleted，Cloud/MES 外部通道已成功或可靠交接。"));
        }
    }

    private async Task<bool> ProcessConsumerAsync(
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
                return await HandleFailureAsync(record, consumer, "consumer_returned_failure", cancellationToken)
                    .ConfigureAwait(false);
            }

            return true;
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
                return false;
            }

            return await _persistenceWriter.PersistNonRetryableAsync(
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
            return await HandleFailureAsync(record, consumer, ResolveFailureReason(ex), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> HandleFailureAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (consumer.FailureMode == ConsumerFailureMode.BestEffort)
        {
            WriteLogBestEffort(() =>
                Logger.Warn(
                    $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                    $"本地消费者={consumer.Name}，结果=Failed，" +
                    $"原因码={errorMessage}；BestEffort 失败不阻塞外部通道交接完成。"));
            return false;
        }

        if (consumer.RetryChannel == DataPipelineRetryChannel.None)
        {
            var details =
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                $"关键消费者={consumer.Name}，结果=Failed，" +
                "原因码=InvalidRetryChannel。";
            WriteLogBestEffort(() => Logger.Error(details));
            _criticalFallbackWriter.Write("DataPipeline.ProcessQueue.InvalidRetryChannel", details);
            return false;
        }

        WriteLogBestEffort(() =>
            Logger.Warn(
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                $"消费者={consumer.Name}，结果=DurableHandoffPending，" +
                $"目标={DataPipelineRetryChannelMetadata.Format(consumer.RetryChannel)}。"));

        var sourceTable = DataPipelineRetryChannelMetadata.TryGetFailedRecordSourceTable(consumer.RetryChannel);

        if (string.IsNullOrWhiteSpace(sourceTable))
        {
            var unsupportedDetails =
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                $"消费者={consumer.Name}，结果=Failed，原因码=UnsupportedRetryChannel，" +
                $"目标={DataPipelineRetryChannelMetadata.Format(consumer.RetryChannel)}。";
            WriteLogBestEffort(() => Logger.Error(unsupportedDetails));
            _criticalFallbackWriter.Write("DataPipeline.ProcessQueue.UnsupportedRetryChannel", unsupportedDetails);
            return false;
        }

        return await _persistenceWriter.PersistAsync(
                record,
                consumer.RetryChannel,
                consumer.Name,
                errorMessage,
                sourceTable,
                DeadLetterStage.FallbackPersist,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ResolveFailureReason(Exception ex)
        => ex is TimeoutException
            ? "consumer_timeout"
            : $"consumer_exception_{ex.GetType().Name}";

    private static string CreateConsumerKey(ICellDataConsumer consumer)
        => DataPipelineCompletionIdentity.CreateConsumerKey(consumer.RetryChannel, consumer.Name);

    private async Task MarkIngressConsumerCompletedAsync(
        IngressConsumerContext context,
        CellCompletedRecord record,
        CancellationToken cancellationToken)
    {
        await _ingressStore!
            .MarkConsumerCompletedAsync(
                context.CompletionId,
                context.ConsumerKey,
                cancellationToken)
            .ConfigureAwait(false);
        var completed = await _ingressStore
            .CompleteIfAllConsumersFinishedAsync(
                context.CompletionId,
                context.RequiredConsumerKeys,
                cancellationToken)
            .ConfigureAwait(false);
        if (completed)
        {
            WriteLogBestEffort(() => Logger.Info(
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                "结果=IngressCompleted，全部必需本地消费与外部持久交接已完成。"));
        }
    }

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
        CancellationToken cancellationToken,
        IngressConsumerContext? ingress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inflightKey = ingress is null
            ? null
            : $"{ingress.CompletionId}|{ingress.ConsumerKey}";
        if (inflightKey is not null
            && !_inflightIngressConsumers.TryAdd(inflightKey, 0))
        {
            return;
        }

        var writer = ResolveDurableQueueWriter(consumer.RetryChannel);
        if (writer is null)
        {
            try
            {
                var handled = await HandleFailureAsync(
                        record,
                        consumer,
                        "durable_outlet_not_configured",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (handled && ingress is not null)
                {
                    await MarkIngressConsumerCompletedAsync(ingress, record, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                RemoveInflight(inflightKey);
            }

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
                accepted = writer.TryWrite(new DurableConsumerWorkItem(record, consumer, ingress));
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
            ? "durable_outlet_worker_stopped"
            : "durable_outlet_queue_full";
        try
        {
            var handled = await HandleFailureAsync(record, consumer, failureMessage, cancellationToken)
                .ConfigureAwait(false);
            if (handled && ingress is not null)
            {
                await MarkIngressConsumerCompletedAsync(ingress, record, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            RemoveInflight(inflightKey);
        }
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
                    var handled = await ProcessConsumerAsync(item.Record, item.Consumer, cancellationToken)
                        .ConfigureAwait(false);
                    if (handled && item.Ingress is not null)
                    {
                        await MarkIngressConsumerCompletedAsync(item.Ingress, item.Record, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    shutdownDeadline ??= CreateShutdownDeadline();
                    if (await PersistShutdownWorkItemAsync(channel, item, shutdownDeadline.Token).ConfigureAwait(false) is { } failure)
                    {
                        shutdownFailures.Add(failure);
                    }
                    else if (item.Ingress is not null)
                    {
                        try
                        {
                            await MarkIngressConsumerCompletedAsync(item.Ingress, item.Record, shutdownDeadline.Token)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            shutdownFailures.Add(ex);
                        }
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    TryWriteUnexpectedDurableFailure(channel, item, ex);
                }
                finally
                {
                    RemoveInflight(item.Ingress is null
                        ? null
                        : $"{item.Ingress.CompletionId}|{item.Ingress.ConsumerKey}");
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
                        else if (queuedItem.Ingress is not null)
                        {
                            try
                            {
                                await MarkIngressConsumerCompletedAsync(
                                        queuedItem.Ingress,
                                        queuedItem.Record,
                                        shutdownDeadline.Token)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                shutdownFailures.Add(ex);
                            }
                        }
                    }
                    finally
                    {
                        RemoveInflight(queuedItem.Ingress is null
                            ? null
                            : $"{queuedItem.Ingress.CompletionId}|{queuedItem.Ingress.ConsumerKey}");
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
            var persisted = await _persistenceWriter.PersistAsync(
                    item.Record,
                    channel,
                    item.Consumer.Name,
                    failureReason,
                    sourceTable,
                    DeadLetterStage.DurableShutdownPersist,
                    cancellationToken: shutdownToken)
                .ConfigureAwait(false);
            if (!persisted)
            {
                return new InvalidOperationException(
                    $"{DataPipelineRetryChannelMetadata.Format(channel)} durable shutdown 未能交接到可消费持久链。");
            }

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
            $"[CorrelationId={DataPipelineCompletionIdentity.Create(record)}][PlcCode={record.ResolvePlcCode()}]" +
            $"[TaskKey={record.TaskKey}][数据管道] 工序={record.CellData.ProcessType} " +
            $"后台出口={DataPipelineRetryChannelMetadata.Format(channel)} 消费异常，" +
            $"模块={record.ModuleId ?? "<unknown>"}，业务标识={record.CellData.DisplayLabel}，" +
            $"结果=Failed，异常类型={exception.GetType().Name}。";
        try
        {
            _criticalFallbackWriter.Write(
                $"DataPipeline.ProcessQueue.{channel}.UnexpectedConsumerFailure",
                details,
                exception);
        }
        catch (Exception criticalEx)
        {
            WriteLogBestEffort(() =>
                Logger.Error($"{details}；critical fallback 写入失败，异常类型={criticalEx.GetType().Name}。"));
        }
    }

    private static void WriteLogBestEffort(Action writeLog)
    {
        try
        {
            writeLog();
        }
        catch
        {
            // A dequeued record must be dispatched or compensated even when a log sink/subscriber fails.
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

    private void RemoveInflight(string? inflightKey)
    {
        if (inflightKey is not null)
        {
            _inflightIngressConsumers.TryRemove(inflightKey, out _);
        }
    }

    private sealed record DurableConsumerWorkItem(
        CellCompletedRecord Record,
        ICellDataConsumer Consumer,
        IngressConsumerContext? Ingress);

    private sealed record IngressConsumerContext(
        string CompletionId,
        string ConsumerKey,
        IReadOnlyCollection<string> RequiredConsumerKeys);

    private sealed class DurableShutdownPersistenceException(
        string message,
        Exception innerException) : Exception(message, innerException);
}
