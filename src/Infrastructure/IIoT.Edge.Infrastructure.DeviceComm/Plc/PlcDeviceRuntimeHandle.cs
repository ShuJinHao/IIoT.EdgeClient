using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Tasks;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcDeviceRuntimeHandle
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly object _stopLock = new();
    private readonly SemaphoreSlim _taskGate = new(1, 1);
    private readonly Dictionary<string, BusinessTaskSlot> _businessTasks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _baseRunningHandles = [];
    private Task? _stopTask;
    private Task? _periodicReadExecution;
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
        lock (_baseRunningHandles)
        {
            _baseRunningHandles.Add(supervisor);
            _baseRunningHandles.Add(connection);
        }
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
            var removedKeys = GetBusinessTaskKeysSnapshot()
                .Where(key => !nextKeys.Contains(key))
                .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var taskKey in removedKeys)
            {
                var slot = GetRequiredBusinessTaskSlot(taskKey);
                await StopBusinessTaskAsync(slot, cancellationToken).ConfigureAwait(false);
                lock (_businessTasks)
                {
                    _businessTasks.Remove(taskKey);
                }
            }

            foreach (var taskKey in nextKeys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
            {
                lock (_businessTasks)
                {
                    if (_businessTasks.TryGetValue(taskKey, out var existing))
                    {
                        if (existing.Task is null)
                        {
                            existing.Factory = plan.GetRequiredFactory(taskKey);
                        }

                        continue;
                    }

                    _businessTasks.Add(
                        taskKey,
                        new BusinessTaskSlot(taskKey, plan.GetRequiredFactory(taskKey)));
                }
            }

            if (!IsConnected)
            {
                return new PlcRuntimeTaskApplyResult(
                    PlcRuntimeTaskApplyState.WaitingForConnection,
                    GetBusinessTaskKeysSnapshot());
            }

            foreach (var taskKey in nextKeys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
            {
                await StartBusinessTaskAsync(
                        GetRequiredBusinessTaskSlot(taskKey),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return new PlcRuntimeTaskApplyResult(
                PlcRuntimeTaskApplyState.Applied,
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
        lock (_baseRunningHandles)
        {
            return _baseRunningHandles.ToArray();
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
                    await StopDependentTasksAsync(cancellationToken).ConfigureAwait(false);
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
            return;
        }

        if (_periodicReadCancellation is not null)
        {
            await StopPeriodicReadTaskAsync(cancellationToken).ConfigureAwait(false);
        }

        var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            CancellationTokenSource.Token);
        try
        {
            var execution = PeriodicReadTask.StartAsync(taskCancellation.Token);
            await Task.Yield();
            if (execution.IsCompleted)
            {
                await execution.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"PLC“{DeviceName}”周期读取任务在启动阶段提前结束。");
            }

            _periodicReadCancellation = taskCancellation;
            _periodicReadExecution = execution;
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
            return;
        }

        if (slot.Cancellation is not null)
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
        try
        {
            var run = startupAware.StartWithStartup(taskCancellation.Token);
            await run.Startup.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (run.Execution.IsCompleted)
            {
                await run.Execution.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"业务任务 {slot.TaskKey} 在启动阶段提前结束。");
            }

            slot.Cancellation = taskCancellation;
            slot.Execution = run.Execution;
        }
        catch
        {
            await taskCancellation.CancelAsync().ConfigureAwait(false);
            taskCancellation.Dispose();
            throw;
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
        if (taskCancellation is null)
        {
            return;
        }

        await taskCancellation.CancelAsync().ConfigureAwait(false);
        Exception? stopFailure = null;
        if (slot.Task is not null)
        {
            try
            {
                await slot.Task.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                stopFailure = ex;
            }
        }

        if (execution is not null)
        {
            await execution.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
        }

        taskCancellation.Dispose();
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
        if (taskCancellation is null)
        {
            return;
        }

        await taskCancellation.CancelAsync().ConfigureAwait(false);
        Exception? stopFailure = null;
        try
        {
            await PeriodicReadTask.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopFailure = ex;
        }

        if (execution is not null)
        {
            await execution.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
        }

        taskCancellation.Dispose();
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

    private sealed class BusinessTaskSlot(
        string taskKey,
        PlcRuntimeBusinessTaskFactory factory)
    {
        public string TaskKey { get; } = taskKey;

        public PlcRuntimeBusinessTaskFactory Factory { get; set; } = factory;

        public IPlcTask? Task { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }

        public Task? Execution { get; set; }
    }
}
