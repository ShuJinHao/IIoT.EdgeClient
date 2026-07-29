using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Tasks;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcDeviceRuntimeHandle
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly object _stopLock = new();
    private readonly SemaphoreSlim _taskGate = new(1, 1);
    private readonly Dictionary<string, BusinessTaskSlot> _businessTasks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _runningHandles = [];
    private Task? _stopTask;
    private Task? _periodicReadExecution;
    private Task? _periodicReadStopExecution;
    private CancellationTokenSource? _periodicReadCancellation;
    private int _started;
    private int _connected;
    private int _cancellationDisposed;

    public required int DeviceId { get; init; }

    public required string DeviceName { get; init; }

    public required IPlcService PlcService { get; init; }

    public required IPlcBuffer Buffer { get; init; }

    public required ProductionContext Context { get; init; }

    public required IPlcTask ConnectionTask { get; init; }

    public required IPlcTask PeriodicReadTask { get; init; }

    public required PlcRuntimeConnectionSignal ConnectionSignal { get; init; }

    public required ILogService Logger { get; init; }

    public required PlcConnectionStatusStore StatusStore { get; init; }

    public required CancellationTokenSource CancellationTokenSource { get; init; }

    public bool IsConnected => Volatile.Read(ref _connected) != 0;

    public IReadOnlyCollection<string> EnabledTaskKeys
    {
        get
        {
            lock (_businessTasks)
            {
                return _businessTasks.Keys
                    .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException($"PLC“{DeviceName}”runtime 不得重复启动。");
        }

        var supervisor = ObserveConnectionStateAsync(CancellationTokenSource.Token);
        var connection = RunConnectionTaskAsync(CancellationTokenSource.Token);
        TrackExecution(supervisor);
        TrackExecution(connection);
    }

    public async Task<PlcRuntimeTaskApplyResult> ApplyTaskPlanAsync(
        PlcRuntimeTaskPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(plan.DeviceName, DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"任务计划设备“{plan.DeviceName}”与 runtime 设备“{DeviceName}”不一致。");
        }

        await _taskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var nextKeys = plan.TaskKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var currentSlots = GetBusinessTaskSlotsSnapshot();
            var removedSlots = currentSlots
                .Where(pair => !nextKeys.Contains(pair.Key))
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => new PreviousTaskState(
                    pair.Value,
                    pair.Value.IsRunning))
                .ToArray();
            var stagedSlots = nextKeys
                .Where(key => !currentSlots.ContainsKey(key))
                .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
                .Select(key => new BusinessTaskSlot(
                    key,
                    plan.GetRequiredFactory(key)))
                .ToArray();
            var factoryChanges = currentSlots
                .Where(pair => nextKeys.Contains(pair.Key) && pair.Value.Task is null)
                .Select(pair => new PreviousTaskFactory(
                    pair.Value,
                    pair.Value.Factory,
                    pair.Value.Task))
                .ToArray();
            foreach (var change in factoryChanges)
            {
                change.Slot.Factory = plan.GetRequiredFactory(change.Slot.TaskKey);
            }

            var startedForApply = new List<BusinessTaskSlot>();
            try
            {
                if (IsConnected)
                {
                    var slotsToStart = currentSlots
                        .Where(pair => nextKeys.Contains(pair.Key) && !pair.Value.IsRunning)
                        .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(static pair => pair.Value)
                        .Concat(stagedSlots)
                        .ToArray();
                    foreach (var slot in slotsToStart)
                    {
                        await StartBusinessTaskAsync(slot, cancellationToken).ConfigureAwait(false);
                        startedForApply.Add(slot);
                    }
                }

                foreach (var previous in removedSlots)
                {
                    await StopBusinessTaskAsync(previous.Slot, cancellationToken).ConfigureAwait(false);
                }

                lock (_businessTasks)
                {
                    foreach (var previous in removedSlots)
                    {
                        _businessTasks.Remove(previous.Slot.TaskKey);
                    }

                    foreach (var slot in stagedSlots)
                    {
                        _businessTasks.Add(slot.TaskKey, slot);
                    }
                }
            }
            catch (Exception applyFailure)
            {
                var rollbackFailures = await RollbackTaskPlanAsync(
                        startedForApply,
                        stagedSlots,
                        removedSlots,
                        factoryChanges)
                    .ConfigureAwait(false);
                if (rollbackFailures.Count > 0)
                {
                    throw new AggregateException(
                        $"PLC“{DeviceName}”任务计划应用失败，且运行时回滚未完整完成。",
                        [applyFailure, .. rollbackFailures]);
                }

                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(applyFailure)
                    .Throw();
            }

            return new PlcRuntimeTaskApplyResult(
                IsConnected
                    ? PlcRuntimeTaskApplyState.Applied
                    : PlcRuntimeTaskApplyState.WaitingForConnection,
                GetBusinessTaskKeysSnapshot());
        }
        finally
        {
            _taskGate.Release();
        }
    }

    public Task RequestStopAsync()
    {
        lock (_stopLock)
        {
            return _stopTask ??= StopCoreAsync();
        }
    }

    public IReadOnlyCollection<Task> GetRunningHandlesSnapshot()
    {
        lock (_runningHandles)
        {
            return _runningHandles.ToArray();
        }
    }

    public IPlcTask? GetBusinessTask(string taskKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        lock (_businessTasks)
        {
            return _businessTasks.TryGetValue(taskKey, out var slot)
                ? slot.Task
                : null;
        }
    }

    public void DisposeCancellation()
    {
        if (Interlocked.Exchange(ref _cancellationDisposed, 1) == 0)
        {
            CancellationTokenSource.Dispose();
            _taskGate.Dispose();
        }
    }

    private async Task RunConnectionTaskAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ConnectionTask.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (PlcServiceQuarantinedException ex)
        {
            ConnectionSignal.Report(false);
            StatusStore.MarkRuntimeFault(DeviceId, DeviceName, ex.Message);
            Logger.Error($"[{DeviceName}] PLC service 已隔离，连接任务已停止：{ex.Message}");
        }
        catch (Exception ex)
        {
            ConnectionSignal.Report(false);
            StatusStore.MarkDisconnected(DeviceId, DeviceName, ex.Message);
            Logger.Error($"[{DeviceName}] PLC 连接任务异常：{ex.Message}");
        }
    }

    private async Task ObserveConnectionStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var isConnected in ConnectionSignal
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                await _taskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (isConnected)
                    {
                        if (Interlocked.Exchange(ref _connected, 1) != 0)
                        {
                            continue;
                        }

                        try
                        {
                            await StartPeriodicReadTaskAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Exchange(ref _connected, 0);
                            StatusStore.MarkRuntimeFault(
                                DeviceId,
                                DeviceName,
                                $"周期读取任务启动失败：{ex.Message}");
                            Logger.Error(
                                $"[{DeviceName}] 周期读取任务启动失败，业务任务保持暂停：{ex.Message}");
                            continue;
                        }

                        foreach (var taskKey in GetBusinessTaskKeysSnapshot()
                                     .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
                        {
                            try
                            {
                                await StartBusinessTaskAsync(
                                        GetRequiredBusinessTaskSlot(taskKey),
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(
                                    $"[{DeviceName}] 业务任务 {taskKey} 启动失败，已仅隔离该 TaskKey：{ex.Message}");
                            }
                        }

                        continue;
                    }

                    Interlocked.Exchange(ref _connected, 0);
                    try
                    {
                        await StopDependentTasksAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(
                            $"[{DeviceName}] 断联后的依赖任务暂停未完整完成，连接监督将继续等待后续状态：{ex.Message}");
                    }
                }
                finally
                {
                    _taskGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _connected, 0);
            StatusStore.MarkRuntimeFault(
                DeviceId,
                DeviceName,
                $"连接状态监督失败：{ex.Message}");
            Logger.Error($"[{DeviceName}] 连接状态监督失败，依赖任务保持暂停：{ex.Message}");
        }
    }

    private async Task StartPeriodicReadTaskAsync(CancellationToken cancellationToken)
    {
        if (_periodicReadExecution is { IsCompleted: false })
        {
            if (_periodicReadCancellation?.IsCancellationRequested == true)
            {
                throw new InvalidOperationException(
                    $"PLC“{DeviceName}”上一周期读取任务尚未安全退出，拒绝重复启动。");
            }

            return;
        }

        if (_periodicReadCancellation is not null
            || _periodicReadStopExecution is not null)
        {
            await StopPeriodicReadTaskAsync(cancellationToken).ConfigureAwait(false);
        }

        var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            CancellationTokenSource.Token);
        try
        {
            var execution = PeriodicReadTask.StartAsync(taskCancellation.Token);
            TrackExecution(execution);
            await Task.Yield();
            if (execution.IsCompleted)
            {
                await execution.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"PLC“{DeviceName}”周期读取任务在启动阶段提前结束。");
            }

            _periodicReadCancellation = taskCancellation;
            _periodicReadExecution = execution;
            _ = ObservePeriodicReadExecutionAsync(
                execution,
                taskCancellation.Token);
        }
        catch
        {
            taskCancellation.Dispose();
            throw;
        }
    }

    private async Task StartBusinessTaskAsync(
        BusinessTaskSlot slot,
        CancellationToken cancellationToken)
    {
        if (slot.Execution is { IsCompleted: false })
        {
            if (slot.Cancellation?.IsCancellationRequested == true)
            {
                throw new InvalidOperationException(
                    $"业务任务 {slot.TaskKey} 上一执行句柄尚未安全退出，拒绝重复启动。");
            }

            return;
        }

        if (slot.Cancellation is not null
            || slot.StopExecution is not null)
        {
            await StopBusinessTaskAsync(slot, cancellationToken).ConfigureAwait(false);
        }

        slot.Task ??= CreateBusinessTask(slot);
        if (slot.Task is not IStartupAwareBackgroundTask startupAware)
        {
            throw new InvalidOperationException(
                $"业务任务 {slot.TaskKey} 未实现 {nameof(IStartupAwareBackgroundTask)}，拒绝无启动握手运行。");
        }

        var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            CancellationTokenSource.Token);
        Task? execution = null;
        try
        {
            var run = startupAware.StartWithStartup(taskCancellation.Token);
            execution = run.Execution;
            slot.Cancellation = taskCancellation;
            slot.Execution = execution;
            TrackExecution(execution);

            Task firstCompletion;
            try
            {
                firstCompletion = await Task
                    .WhenAny(run.Startup, execution)
                    .WaitAsync(StartupTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"业务任务 {slot.TaskKey} 启动握手超过 {StartupTimeout.TotalSeconds:0} 秒，已失败关闭。",
                    ex);
            }

            if (ReferenceEquals(firstCompletion, execution) && !run.Startup.IsCompleted)
            {
                await execution.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"业务任务 {slot.TaskKey} 在启动握手完成前提前结束。");
            }

            await run.Startup.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (run.Execution.IsCompleted)
            {
                await run.Execution.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"业务任务 {slot.TaskKey} 在启动阶段提前结束。");
            }

            slot.UnexpectedExitReason = null;
            _ = ObserveBusinessExecutionAsync(
                slot,
                execution,
                taskCancellation.Token);
        }
        catch (Exception startFailure)
        {
            Exception? cleanupFailure = null;
            try
            {
                await StopBusinessTaskAsync(slot, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }

            if (execution is null)
            {
                slot.Cancellation = null;
                slot.Execution = null;
                taskCancellation.Dispose();
            }

            if (cleanupFailure is not null)
            {
                throw new AggregateException(
                    $"业务任务 {slot.TaskKey} 启动失败，且启动清理未完成。",
                    startFailure,
                    cleanupFailure);
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(startFailure)
                .Throw();
        }
    }

    private async Task ObserveBusinessExecutionAsync(
        BusinessTaskSlot slot,
        Task execution,
        CancellationToken taskCancellationToken)
    {
        try
        {
            await execution.ConfigureAwait(false);
            if (!ReferenceEquals(slot.Execution, execution))
            {
                return;
            }

            if (!taskCancellationToken.IsCancellationRequested)
            {
                slot.UnexpectedExitReason = "任务执行句柄在 PLC 仍连接时提前结束。";
                Logger.Error(
                    $"[{DeviceName}] 业务任务 {slot.TaskKey} 意外停止：{slot.UnexpectedExitReason}");
            }
        }
        catch (OperationCanceledException) when (taskCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(slot.Execution, execution))
            {
                return;
            }

            slot.UnexpectedExitReason = ex.Message;
            Logger.Error(
                $"[{DeviceName}] 业务任务 {slot.TaskKey} 执行故障，已仅隔离该 TaskKey：{ex.Message}");
        }
    }

    private async Task ObservePeriodicReadExecutionAsync(
        Task execution,
        CancellationToken taskCancellationToken)
    {
        try
        {
            await execution.ConfigureAwait(false);
            if (!ReferenceEquals(_periodicReadExecution, execution))
            {
                return;
            }

            if (!taskCancellationToken.IsCancellationRequested)
            {
                StatusStore.MarkRuntimeFault(
                    DeviceId,
                    DeviceName,
                    "周期读取任务在 PLC 仍连接时提前结束。");
                Logger.Error($"[{DeviceName}] 周期读取任务意外停止，业务任务将暂停。");
                ConnectionSignal.Report(false);
            }
        }
        catch (OperationCanceledException) when (taskCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_periodicReadExecution, execution))
            {
                return;
            }

            StatusStore.MarkRuntimeFault(
                DeviceId,
                DeviceName,
                $"周期读取任务执行故障：{ex.Message}");
            Logger.Error($"[{DeviceName}] 周期读取任务执行故障，业务任务将暂停：{ex.Message}");
            ConnectionSignal.Report(false);
        }
    }

    private IPlcTask CreateBusinessTask(BusinessTaskSlot slot)
    {
        var task = slot.Factory(Buffer, Context)
                   ?? throw new InvalidOperationException(
                       $"业务任务工厂为 {slot.TaskKey} 返回了 null。");
        if (!string.Equals(task.TaskName, slot.TaskKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"业务任务工厂请求 {slot.TaskKey}，但返回任务名 {task.TaskName}。");
        }

        return task;
    }

    private async Task<IReadOnlyCollection<Exception>> RollbackTaskPlanAsync(
        IReadOnlyCollection<BusinessTaskSlot> startedForApply,
        IReadOnlyCollection<BusinessTaskSlot> stagedSlots,
        IReadOnlyCollection<PreviousTaskState> removedSlots,
        IReadOnlyCollection<PreviousTaskFactory> factoryChanges)
    {
        var failures = new List<Exception>();
        var stoppedSlots = new HashSet<BusinessTaskSlot>();
        var rollbackStopSlots = startedForApply
            .Concat(stagedSlots)
            .Concat(factoryChanges.Select(static change => change.Slot))
            .Where(static slot => slot.Cancellation is not null)
            .Distinct()
            .Reverse()
            .ToArray();
        foreach (var slot in rollbackStopSlots)
        {
            try
            {
                await StopBusinessTaskAsync(slot, CancellationToken.None).ConfigureAwait(false);
                stoppedSlots.Add(slot);
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException(
                    $"回滚新增或恢复任务 {slot.TaskKey} 失败：{ex.Message}",
                    ex));
            }
        }

        foreach (var change in factoryChanges)
        {
            change.Slot.Factory = change.Factory;
            if (!change.Slot.IsRunning)
            {
                change.Slot.Task = change.Task;
            }
        }

        foreach (var previous in removedSlots.Where(static state => state.WasRunning))
        {
            if (previous.Slot.IsRunning)
            {
                continue;
            }

            try
            {
                await StartBusinessTaskAsync(previous.Slot, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException(
                    $"回滚原任务 {previous.Slot.TaskKey} 失败：{ex.Message}",
                    ex));
            }
        }

        lock (_businessTasks)
        {
            foreach (var slot in stagedSlots)
            {
                if (!stoppedSlots.Contains(slot) && slot.IsRunning)
                {
                    _businessTasks.TryAdd(slot.TaskKey, slot);
                }
            }
        }

        return failures;
    }

    private async Task StopDependentTasksAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (var taskKey in GetBusinessTaskKeysSnapshot()
                     .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await StopBusinessTaskAsync(
                        GetRequiredBusinessTaskSlot(taskKey),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
                Logger.Error($"[{DeviceName}] 业务任务 {taskKey} 暂停失败：{ex.Message}");
            }
        }

        try
        {
            await StopPeriodicReadTaskAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
            Logger.Error($"[{DeviceName}] 周期读取任务暂停失败：{ex.Message}");
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                $"PLC“{DeviceName}”断联后存在未安全暂停的任务。",
                failures);
        }
    }

    private async Task StopBusinessTaskAsync(
        BusinessTaskSlot slot,
        CancellationToken cancellationToken)
    {
        var taskCancellation = slot.Cancellation;
        var execution = slot.Execution;
        var stopExecution = slot.StopExecution;
        if (taskCancellation is null && stopExecution is null)
        {
            return;
        }

        if (taskCancellation is not null)
        {
            await taskCancellation.CancelAsync().ConfigureAwait(false);
        }

        Exception? stopFailure = null;
        if (slot.Task is not null && stopExecution is null)
        {
            try
            {
                stopExecution = slot.Task.StopAsync(cancellationToken);
                slot.StopExecution = stopExecution;
                TrackExecution(stopExecution);
            }
            catch (Exception ex)
            {
                stopFailure = ex;
            }
        }

        if (stopExecution is not null)
        {
            try
            {
                await stopExecution
                    .WaitAsync(StopTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"业务任务 {slot.TaskKey} 停止钩子超过 {StopTimeout.TotalSeconds:0} 秒，runtime 将保留隔离。",
                    ex);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopFailure = stopFailure is null
                    ? ex
                    : new AggregateException(stopFailure, ex);
            }
            finally
            {
                if (stopExecution.IsCompleted)
                {
                    slot.StopExecution = null;
                }
            }
        }

        if (execution is not null)
        {
            try
            {
                await execution.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (OperationCanceledException) when (
                taskCancellation?.IsCancellationRequested == true
                && execution.IsCompleted
                && !cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (
                execution.IsCompleted
                && !cancellationToken.IsCancellationRequested)
            {
                // 执行故障由任务观察器按 TaskKey 记录；任务已经退出，不再把同一故障重复当作停止失败。
            }
        }

        taskCancellation?.Dispose();
        slot.Cancellation = null;
        slot.Execution = null;
        if (stopFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(stopFailure)
                .Throw();
        }
    }

    private async Task StopPeriodicReadTaskAsync(CancellationToken cancellationToken)
    {
        var taskCancellation = _periodicReadCancellation;
        var execution = _periodicReadExecution;
        var stopExecution = _periodicReadStopExecution;
        if (taskCancellation is null && stopExecution is null)
        {
            return;
        }

        if (taskCancellation is not null)
        {
            await taskCancellation.CancelAsync().ConfigureAwait(false);
        }

        Exception? stopFailure = null;
        if (stopExecution is null)
        {
            try
            {
                stopExecution = PeriodicReadTask.StopAsync(cancellationToken);
                _periodicReadStopExecution = stopExecution;
                TrackExecution(stopExecution);
            }
            catch (Exception ex)
            {
                stopFailure = ex;
            }
        }

        if (stopExecution is not null)
        {
            try
            {
                await stopExecution
                    .WaitAsync(StopTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"PLC“{DeviceName}”周期读取停止钩子超过 {StopTimeout.TotalSeconds:0} 秒，runtime 将保留隔离。",
                    ex);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopFailure = stopFailure is null
                    ? ex
                    : new AggregateException(stopFailure, ex);
            }
            finally
            {
                if (stopExecution.IsCompleted)
                {
                    _periodicReadStopExecution = null;
                }
            }
        }

        if (execution is not null)
        {
            try
            {
                await execution.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (OperationCanceledException) when (
                taskCancellation?.IsCancellationRequested == true
                && execution.IsCompleted
                && !cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception) when (
                execution.IsCompleted
                && !cancellationToken.IsCancellationRequested)
            {
                // 执行故障已由周期读取观察器记录；这里只确认句柄已退出。
            }
        }

        taskCancellation?.Dispose();
        _periodicReadCancellation = null;
        _periodicReadExecution = null;
        if (stopFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(stopFailure)
                .Throw();
        }
    }

    private async Task StopCoreAsync()
    {
        await CancellationTokenSource.CancelAsync().ConfigureAwait(false);
        ConnectionSignal.Complete();

        await _taskGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Interlocked.Exchange(ref _connected, 0);
            await StopDependentTasksAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _taskGate.Release();
        }
    }

    private IReadOnlyCollection<string> GetBusinessTaskKeysSnapshot()
    {
        lock (_businessTasks)
        {
            return _businessTasks.Keys.ToArray();
        }
    }

    private IReadOnlyDictionary<string, BusinessTaskSlot> GetBusinessTaskSlotsSnapshot()
    {
        lock (_businessTasks)
        {
            return new Dictionary<string, BusinessTaskSlot>(
                _businessTasks,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private BusinessTaskSlot GetRequiredBusinessTaskSlot(string taskKey)
    {
        lock (_businessTasks)
        {
            return _businessTasks.TryGetValue(taskKey, out var slot)
                ? slot
                : throw new InvalidOperationException(
                    $"PLC“{DeviceName}”runtime 中不存在业务任务 {taskKey}。");
        }
    }

    private void TrackExecution(Task execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        lock (_runningHandles)
        {
            _runningHandles.Add(execution);
        }

        _ = execution.ContinueWith(
            static (completed, state) =>
            {
                var runtime = (PlcDeviceRuntimeHandle)state!;
                lock (runtime._runningHandles)
                {
                    runtime._runningHandles.Remove(completed);
                }
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record PreviousTaskState(
        BusinessTaskSlot Slot,
        bool WasRunning);

    private sealed record PreviousTaskFactory(
        BusinessTaskSlot Slot,
        PlcRuntimeBusinessTaskFactory Factory,
        IPlcTask? Task);

    private sealed class BusinessTaskSlot(
        string taskKey,
        PlcRuntimeBusinessTaskFactory factory)
    {
        public string TaskKey { get; } = taskKey;

        public PlcRuntimeBusinessTaskFactory Factory { get; set; } = factory;

        public IPlcTask? Task { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }

        public Task? Execution { get; set; }

        public Task? StopExecution { get; set; }

        public string? UnexpectedExitReason { get; set; }

        public bool IsRunning => Execution is { IsCompleted: false };
    }
}
