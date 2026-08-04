using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Checkpoints;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;

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
    private StopDeadline? _shutdownStopDeadline;
    private int _started;
    private int _connected;
    private int _periodicReadAvailable;
    private int _runtimeQuarantined;
    private int _cancellationDisposed;

    public required int DeviceId { get; init; }

    public required string PlcCode { get; init; }

    public required string DeviceName { get; init; }

    public required IPlcService PlcService { get; init; }

    public required IPlcBuffer Buffer { get; init; }

    public required ProductionContext Context { get; init; }

    public required IPlcTask ConnectionTask { get; init; }

    public required IPlcTask PeriodicReadTask { get; init; }

    public required PlcRuntimeConnectionSignal ConnectionSignal { get; init; }

    public required ILogService Logger { get; init; }

    public required PlcConnectionStatusStore StatusStore { get; init; }

    public IPlcTaskRuntimeStatusWriter? TaskStatusWriter { get; init; }

    public required CancellationTokenSource CancellationTokenSource { get; init; }

    internal TimeProvider TransitionTimeProvider { get; init; } = TimeProvider.System;

    internal TimeSpan GetRemainingShutdownTimeout()
        => _shutdownStopDeadline?.Remaining ?? StopTimeout;

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
            throw new InvalidOperationException($"PLC“{PlcCode}”runtime 不得重复启动。");
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
        if (plan.NetworkDeviceId != DeviceId
            || !string.Equals(plan.PlcCode, PlcCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"任务计划 PLC“{plan.PlcCode}”(NetworkDeviceId={plan.NetworkDeviceId}, DeviceName={plan.DeviceName})"
                + $"与 runtime PLC“{PlcCode}”(NetworkDeviceId={DeviceId}, DeviceName={DeviceName}) 不一致。");
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
                    plan.GetRequiredEntry(key)))
                .ToArray();
            var startedForApply = new List<BusinessTaskSlot>();
            try
            {
                if (IsConnected)
                {
                    foreach (var slot in stagedSlots.Where(CanStartBusinessTask))
                    {
                        await StartBusinessTaskAsync(slot, cancellationToken).ConfigureAwait(false);
                        startedForApply.Add(slot);
                    }
                }

                foreach (var previous in removedSlots)
                {
                    await StopBusinessTaskAsync(
                            previous.Slot,
                            cancellationToken,
                            disposition: previous.WasRunning
                                ? BusinessTaskStopDisposition.PreserveStopping
                                : BusinessTaskStopDisposition.PreserveState,
                            checkpointSaveReason: PlcTaskCheckpointSaveReason.TaskDisabled)
                        .ConfigureAwait(false);
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

                    foreach (var pair in currentSlots.Where(pair => nextKeys.Contains(pair.Key)))
                    {
                        pair.Value.UpdatePlanEntry(plan.GetRequiredEntry(pair.Key));
                    }
                }

                foreach (var previous in removedSlots)
                {
                    TaskStatusWriter?.Remove(PlcCode, previous.Slot.TaskKey);
                }

                if (!IsConnected)
                {
                    foreach (var slot in stagedSlots)
                    {
                        SetTaskDisconnectedState(slot.TaskKey);
                    }
                }
                else if (!IsPeriodicReadAvailable)
                {
                    foreach (var slot in stagedSlots.Where(static slot => slot.RequiresPeriodicRead))
                    {
                        SetTaskState(
                            slot.TaskKey,
                            PlcTaskRuntimeState.Faulted,
                            PlcTaskRuntimeErrorCodes.PeriodicReadFault);
                    }
                }
            }
            catch (Exception applyFailure)
            {
                var rollbackFailures = await RollbackTaskPlanAsync(
                        startedForApply,
                        stagedSlots,
                        removedSlots)
                    .ConfigureAwait(false);
                if (rollbackFailures.Count > 0)
                {
                    throw new AggregateException(
                        $"PLC“{PlcCode}”任务计划应用失败，且运行时回滚未完整完成。",
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
            MarkRuntimeQuarantined(ex);
            ConnectionSignal.Report(false);
            Logger.Error(
                $"[PlcCode={PlcCode}] PLC service 已隔离，连接任务已停止，"
                + $"原因码={PlcServiceQuarantinedException.StableReasonCode}，异常类型={ex.GetType().Name}。");
        }
        catch (Exception ex) when (
            PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = PlcOperationFailureClassifier.Classify(ex);
            if (failure.DisconnectsTransport)
            {
                await HandleObservedTransportFailureAsync(failure)
                    .ConfigureAwait(false);
            }
            else
            {
                StatusStore.MarkRuntimeFault(
                    DeviceId,
                    PlcCode,
                    DeviceName,
                    failure.ReasonCode,
                    preserveTransportConnection: true);
                SetAllTaskStates(
                    PlcTaskRuntimeState.Faulted,
                    PlcTaskRuntimeErrorCodes.ConnectionTaskFault,
                    failure.ExceptionType);
            }

            Logger.Error(
                $"[PlcCode={PlcCode}] PLC 连接任务异常，{failure.SafeDiagnostic}。");
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
                        if (IsRuntimeQuarantined)
                        {
                            ConnectionSignal.Report(false);
                            continue;
                        }

                        if (Interlocked.Exchange(ref _connected, 1) != 0)
                        {
                            continue;
                        }

                        try
                        {
                            await StartPeriodicReadTaskAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (
                            PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            var failure = PlcOperationFailureClassifier.Classify(ex);
                            if (failure.DisconnectsTransport)
                            {
                                Interlocked.Exchange(ref _connected, 0);
                                await HandleObservedTransportFailureAsync(failure)
                                    .ConfigureAwait(false);
                                Logger.Error(
                                    $"[PlcCode={PlcCode}] 周期读取任务启动遇到 transport 断联，"
                                    + $"业务任务保持暂停，{failure.SafeDiagnostic}。");
                            }
                            else
                            {
                                Interlocked.Exchange(ref _periodicReadAvailable, 0);
                                StatusStore.MarkRuntimeFault(
                                    DeviceId,
                                    PlcCode,
                                    DeviceName,
                                    PlcTaskRuntimeErrorCodes.PeriodicReadFault,
                                    preserveTransportConnection: true);
                                SetPeriodicReadDependentTaskStates(
                                    PlcTaskRuntimeState.Faulted,
                                    PlcTaskRuntimeErrorCodes.PeriodicReadFault,
                                    failure.ExceptionType);
                                Logger.Error(
                                    $"[PlcCode={PlcCode}] 周期读取任务启动失败，"
                                    + "仅依赖周期读取的业务任务保持暂停，"
                                    + $"原因码={PlcTaskRuntimeErrorCodes.PeriodicReadFault}，"
                                    + $"异常类型={failure.ExceptionType}。");
                            }

                            if (failure.DisconnectsTransport)
                            {
                                continue;
                            }
                        }

                        foreach (var slot in GetBusinessTaskSlotsSnapshot()
                                     .Values
                                     .Where(CanStartBusinessTask)
                                     .OrderBy(static slot => slot.TaskKey, StringComparer.OrdinalIgnoreCase))
                        {
                            try
                            {
                                await StartBusinessTaskAsync(
                                        slot,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception ex) when (
                                PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                var failure = PlcOperationFailureClassifier.Classify(ex);
                                if (failure.DisconnectsTransport)
                                {
                                    Logger.Error(
                                        $"[PlcCode={PlcCode}] 业务任务 {slot.TaskKey} 启动遇到 transport 断联，"
                                        + $"已触发连接释放和全部依赖任务暂停，{failure.SafeDiagnostic}。");
                                    break;
                                }

                                var safeDetail = ex is TimeoutException
                                    ? $"启动握手超过 {StartupTimeout.TotalSeconds:0} 秒，"
                                    : string.Empty;
                                Logger.Error(
                                    $"[PlcCode={PlcCode}] 业务任务 {slot.TaskKey} 启动失败，"
                                    + $"已仅隔离该 TaskKey，{safeDetail}{failure.SafeDiagnostic}。");
                            }
                        }

                        continue;
                    }

                    Interlocked.Exchange(ref _connected, 0);
                    try
                    {
                        await StopDependentTasksAsync(
                                CreateStopDeadline(),
                                cancellationToken,
                                IsRuntimeQuarantined
                                    ? BusinessTaskStopDisposition.PreserveState
                                    : BusinessTaskStopDisposition.WaitingForConnection)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (
                        PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var failure = PlcOperationFailureClassifier.Classify(ex);
                        Logger.Error(
                            $"[PlcCode={PlcCode}] 断联后的依赖任务暂停未完整完成，"
                            + $"连接监督将继续等待后续状态，{failure.SafeDiagnostic}。");
                    }
                    finally
                    {
                        if (IsRuntimeQuarantined)
                        {
                            SetAllTaskStates(
                                PlcTaskRuntimeState.Faulted,
                                PlcTaskRuntimeErrorCodes.RuntimeQuarantined,
                                nameof(PlcServiceQuarantinedException));
                        }
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
        catch (Exception ex) when (
            PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _connected, 0);
            var failure = PlcOperationFailureClassifier.Classify(ex);
            StatusStore.MarkRuntimeFault(
                DeviceId,
                PlcCode,
                DeviceName,
                PlcTaskRuntimeErrorCodes.ConnectionSupervisorFault,
                preserveTransportConnection: true);
            SetAllTaskStates(
                PlcTaskRuntimeState.Faulted,
                PlcTaskRuntimeErrorCodes.ConnectionSupervisorFault,
                failure.ExceptionType);
            Logger.Error(
                $"[PlcCode={PlcCode}] 连接状态监督失败，依赖任务保持暂停，"
                + $"原因码={PlcTaskRuntimeErrorCodes.ConnectionSupervisorFault}，"
                + $"异常类型={failure.ExceptionType}。");
        }
    }

    private async Task StartPeriodicReadTaskAsync(CancellationToken cancellationToken)
    {
        if (_periodicReadExecution is { IsCompleted: false })
        {
            if (_periodicReadCancellation?.IsCancellationRequested == true)
            {
                throw new InvalidOperationException(
                    $"PLC“{PlcCode}”上一周期读取任务尚未安全退出，拒绝重复启动。");
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
                    $"PLC“{PlcCode}”周期读取任务在启动阶段提前结束。");
            }

            _periodicReadCancellation = taskCancellation;
            _periodicReadExecution = execution;
            Interlocked.Exchange(ref _periodicReadAvailable, 1);
            var observer = ObservePeriodicReadExecutionAsync(
                execution,
                taskCancellation.Token);
            TrackExecution(observer);
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
            await StopBusinessTaskAsync(
                    slot,
                    cancellationToken,
                    disposition: BusinessTaskStopDisposition.PreserveState)
                .ConfigureAwait(false);
        }

        CancellationTokenSource? taskCancellation = null;
        Task? execution = null;
        SetTaskState(slot.TaskKey, PlcTaskRuntimeState.Starting);
        try
        {
            slot.Task ??= CreateBusinessTask(slot);
            if (slot.Task is not IStartupAwareBackgroundTask startupAware)
            {
                throw new InvalidOperationException(
                    $"业务任务 {slot.TaskKey} 未实现 {nameof(IStartupAwareBackgroundTask)}，拒绝无启动握手运行。");
            }

            taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                CancellationTokenSource.Token);
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
                    .WaitAsync(
                        StartupTimeout,
                        TransitionTimeProvider,
                        cancellationToken)
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
            slot.StopFailureReason = null;
            SetTaskState(slot.TaskKey, PlcTaskRuntimeState.Running);
            _ = ObserveBusinessExecutionAsync(
                slot,
                execution,
                taskCancellation.Token);
        }
        catch (Exception startFailure)
        {
            var failure = PlcOperationFailureClassifier.Classify(startFailure);
            var callerCancellation = PlcOperationFailureClassifier.IsCallerCancellation(
                startFailure,
                cancellationToken);
            if (callerCancellation)
            {
                SetTaskState(slot.TaskKey, PlcTaskRuntimeState.Stopping);
            }
            else
            {
                SetTaskState(
                    slot.TaskKey,
                    PlcTaskRuntimeState.Faulted,
                    PlcTaskRuntimeErrorCodes.TaskStartFailed,
                    failure.ExceptionType);
            }

            Exception? cleanupFailure = null;
            try
            {
                await StopBusinessTaskAsync(
                        slot,
                        CancellationToken.None,
                        disposition: callerCancellation
                            ? BusinessTaskStopDisposition.PreserveStopping
                            : BusinessTaskStopDisposition.PreserveState)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }

            if (execution is null)
            {
                slot.Cancellation = null;
                slot.Execution = null;
                taskCancellation?.Dispose();
            }

            if (cleanupFailure is not null)
            {
                throw new AggregateException(
                    $"业务任务 {slot.TaskKey} 启动失败，且启动清理未完成。",
                    startFailure,
                    cleanupFailure);
            }

            if (!callerCancellation && failure.DisconnectsTransport)
            {
                await HandleObservedTransportFailureAsync(failure)
                    .ConfigureAwait(false);
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
                slot.UnexpectedExitReason = PlcTaskRuntimeErrorCodes.TaskUnexpectedExit;
                SetTaskState(
                    slot.TaskKey,
                    PlcTaskRuntimeState.Faulted,
                    PlcTaskRuntimeErrorCodes.TaskUnexpectedExit);
                Logger.Error(
                    $"[PlcCode={PlcCode}] 业务任务 {slot.TaskKey} 意外停止，"
                    + $"原因码={PlcTaskRuntimeErrorCodes.TaskUnexpectedExit}。");
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

            var failure = PlcOperationFailureClassifier.Classify(ex);
            slot.UnexpectedExitReason = failure.ReasonCode;
            SetTaskState(
                slot.TaskKey,
                PlcTaskRuntimeState.Faulted,
                failure.ReasonCode,
                failure.ExceptionType);
            if (failure.DisconnectsTransport)
            {
                Logger.Error(
                    $"[PlcCode={PlcCode}] 业务任务 {slot.TaskKey} 执行遇到 transport 断联，"
                    + $"已触发连接释放和全部依赖任务暂停，{failure.SafeDiagnostic}。");
                await HandleObservedTransportFailureAsync(failure).ConfigureAwait(false);
                return;
            }

            Logger.Error(
                $"[PlcCode={PlcCode}] 业务任务 {slot.TaskKey} 执行故障，"
                + $"已仅隔离该 TaskKey，{failure.SafeDiagnostic}。");
        }
    }

    private async Task HandleObservedTransportFailureAsync(
        PlcOperationFailure failure)
    {
        Interlocked.Exchange(ref _connected, 0);
        Interlocked.Exchange(ref _periodicReadAvailable, 0);
        StatusStore.MarkDisconnected(
            DeviceId,
            PlcCode,
            DeviceName,
            failure.ReasonCode);
        ConnectionSignal.Report(false);

        try
        {
            await PlcService.DisconnectAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (PlcServiceQuarantinedException ex)
        {
            MarkRuntimeQuarantined(ex);
            Logger.Error(
                $"[PlcCode={PlcCode}] transport 断联后的连接释放进入隔离，"
                + $"原因码={PlcServiceQuarantinedException.StableReasonCode}，"
                + $"异常类型={ex.GetType().Name}。");
        }
        catch (Exception ex)
        {
            var disconnectFailure = PlcOperationFailureClassifier.Classify(ex);
            Logger.Error(
                $"[PlcCode={PlcCode}] transport 断联后的连接释放失败，"
                + $"{disconnectFailure.SafeDiagnostic}；连接监督保持断联并等待重试。");
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
                await HandlePeriodicReadFaultAsync(
                        execution,
                        exceptionType: null,
                        "意外停止")
                    .ConfigureAwait(false);
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

            var failure = PlcOperationFailureClassifier.Classify(ex);
            if (failure.DisconnectsTransport)
            {
                Interlocked.Exchange(ref _periodicReadAvailable, 0);
                Logger.Error(
                    $"[PlcCode={PlcCode}] 周期读取任务执行遇到 transport 断联，"
                    + $"已触发连接释放和依赖任务暂停，{failure.SafeDiagnostic}。");
                await HandleObservedTransportFailureAsync(failure)
                    .ConfigureAwait(false);
                return;
            }

            await HandlePeriodicReadFaultAsync(
                    execution,
                    failure.ExceptionType,
                    "执行故障")
                .ConfigureAwait(false);
        }
    }

    private async Task HandlePeriodicReadFaultAsync(
        Task execution,
        string? exceptionType,
        string eventName)
    {
        await _taskGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_periodicReadExecution, execution))
            {
                return;
            }

            Interlocked.Exchange(ref _periodicReadAvailable, 0);
            StatusStore.MarkRuntimeFault(
                DeviceId,
                PlcCode,
                DeviceName,
                PlcTaskRuntimeErrorCodes.PeriodicReadFault,
                preserveTransportConnection: true);

            var deadline = CreateStopDeadline();
            var dependentSlots = GetBusinessTaskSlotsSnapshot()
                .Values
                .Where(static slot => slot.RequiresPeriodicRead && slot.IsRunning)
                .OrderBy(static slot => slot.TaskKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await Task.WhenAll(dependentSlots.Select(PauseDependentTaskAsync))
                .ConfigureAwait(false);

            var exceptionDetail = string.IsNullOrWhiteSpace(exceptionType)
                ? string.Empty
                : $"，异常类型={exceptionType}";
            Logger.Error(
                $"[PlcCode={PlcCode}] 周期读取任务{eventName}，"
                + $"已暂停 {dependentSlots.Length} 个读依赖 TaskKey，"
                + $"原因码={PlcTaskRuntimeErrorCodes.PeriodicReadFault}{exceptionDetail}。");

            async Task PauseDependentTaskAsync(BusinessTaskSlot slot)
            {
                try
                {
                    await StopBusinessTaskAsync(
                            slot,
                            CancellationToken.None,
                            deadline,
                            BusinessTaskStopDisposition.PreserveStopping)
                        .ConfigureAwait(false);
                    SetTaskState(
                        slot.TaskKey,
                        PlcTaskRuntimeState.Faulted,
                        PlcTaskRuntimeErrorCodes.PeriodicReadFault,
                        exceptionType);
                }
                catch (Exception ex)
                {
                    var stopFailure = PlcOperationFailureClassifier.Classify(ex);
                    Logger.Error(
                        $"[PlcCode={PlcCode}] 读依赖任务 {slot.TaskKey} 暂停失败，"
                        + $"已保留更具体的停止错误码，{stopFailure.SafeDiagnostic}。");
                }
            }
        }
        finally
        {
            _taskGate.Release();
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
        IReadOnlyCollection<PreviousTaskState> removedSlots)
    {
        var failures = new List<Exception>();
        var stoppedSlots = new HashSet<BusinessTaskSlot>();
        var failedRollbackSlots = new HashSet<BusinessTaskSlot>();
        var rollbackStopSlots = startedForApply
            .Concat(stagedSlots)
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
                failedRollbackSlots.Add(slot);
                failures.Add(new InvalidOperationException(
                    $"回滚新增或恢复任务 {slot.TaskKey} 失败，异常类型={ex.GetType().Name}。",
                    ex));
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
                    $"回滚原任务 {previous.Slot.TaskKey} 失败，异常类型={ex.GetType().Name}。",
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

        foreach (var slot in stagedSlots.Where(slot => !failedRollbackSlots.Contains(slot)))
        {
            TaskStatusWriter?.Remove(PlcCode, slot.TaskKey);
        }

        return failures;
    }

    private async Task StopDependentTasksAsync(
        StopDeadline deadline,
        CancellationToken cancellationToken,
        BusinessTaskStopDisposition businessTaskDisposition,
        PlcTaskCheckpointSaveReason? checkpointSaveReason = null)
    {
        var stopTasks = GetBusinessTaskKeysSnapshot()
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .Select(StopBusinessForTransitionAsync)
            .Append(StopPeriodicReadForTransitionAsync())
            .ToArray();
        var failures = (await Task.WhenAll(stopTasks).ConfigureAwait(false))
            .Where(static failure => failure is not null)
            .Select(static failure => failure!)
            .ToArray();

        if (failures.Length > 0)
        {
            if (cancellationToken.IsCancellationRequested
                && failures.All(failure =>
                    PlcOperationFailureClassifier.IsOnlyCallerCancellation(
                        failure,
                        cancellationToken)))
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new AggregateException(
                $"PLC“{PlcCode}”断联后存在未安全暂停的任务。",
                failures);
        }

        async Task<Exception?> StopBusinessForTransitionAsync(string taskKey)
        {
            try
            {
                await StopBusinessTaskAsync(
                        GetRequiredBusinessTaskSlot(taskKey),
                        cancellationToken,
                        deadline,
                        businessTaskDisposition,
                        checkpointSaveReason)
                    .ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                var failure = PlcOperationFailureClassifier.Classify(ex);
                var safeDetail = ex is TimeoutException
                    ? $"停止钩子超过 {StopTimeout.TotalSeconds:0} 秒，"
                    : string.Empty;
                Logger.Error(
                    $"[PlcCode={PlcCode}] 业务任务 {taskKey} 暂停失败，"
                    + $"{safeDetail}{failure.SafeDiagnostic}。");
                return ex;
            }
        }

        async Task<Exception?> StopPeriodicReadForTransitionAsync()
        {
            try
            {
                await StopPeriodicReadTaskAsync(cancellationToken, deadline)
                    .ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                var failure = PlcOperationFailureClassifier.Classify(ex);
                Logger.Error(
                    $"[PlcCode={PlcCode}] 周期读取任务暂停失败，{failure.SafeDiagnostic}。");
                return ex;
            }
        }
    }

    private async Task StopBusinessTaskAsync(
        BusinessTaskSlot slot,
        CancellationToken cancellationToken,
        StopDeadline? deadline = null,
        BusinessTaskStopDisposition disposition = BusinessTaskStopDisposition.Remove,
        PlcTaskCheckpointSaveReason? checkpointSaveReason = null)
    {
        deadline ??= CreateStopDeadline();
        if (disposition != BusinessTaskStopDisposition.PreserveState)
        {
            SetTaskState(slot.TaskKey, PlcTaskRuntimeState.Stopping);
        }

        var taskCancellation = slot.Cancellation;
        var execution = slot.Execution;
        var stopExecution = slot.StopExecution;
        if (taskCancellation is null && stopExecution is null)
        {
            await SaveTaskCheckpointIfRequiredAsync(
                    slot,
                    checkpointSaveReason,
                    deadline,
                    cancellationToken)
                .ConfigureAwait(false);
            slot.StopFailureReason = null;
            CompleteTaskStop(slot.TaskKey, disposition);
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
            catch (Exception ex) when (
                PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
            {
                throw;
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
                    .WaitAsync(
                        deadline.Remaining,
                        TransitionTimeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                slot.StopFailureReason = PlcTaskRuntimeErrorCodes.TaskStopTimeout;
                SetTaskState(
                    slot.TaskKey,
                    PlcTaskRuntimeState.Faulted,
                    PlcTaskRuntimeErrorCodes.TaskStopTimeout,
                    ex.GetType().Name);
                throw new TimeoutException(
                    $"业务任务 {slot.TaskKey} 停止钩子超过 {StopTimeout.TotalSeconds:0} 秒，runtime 将保留隔离。",
                    ex);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
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
                await execution
                    .WaitAsync(
                        deadline.Remaining,
                        TransitionTimeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                slot.StopFailureReason = PlcTaskRuntimeErrorCodes.TaskStopTimeout;
                SetTaskState(
                    slot.TaskKey,
                    PlcTaskRuntimeState.Faulted,
                    PlcTaskRuntimeErrorCodes.TaskStopTimeout,
                    nameof(TimeoutException));
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
            var failure = PlcOperationFailureClassifier.Classify(stopFailure);
            slot.StopFailureReason = PlcTaskRuntimeErrorCodes.TaskStopFailed;
            SetTaskState(
                slot.TaskKey,
                PlcTaskRuntimeState.Faulted,
                PlcTaskRuntimeErrorCodes.TaskStopFailed,
                failure.ExceptionType);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(stopFailure)
                .Throw();
        }

        await SaveTaskCheckpointIfRequiredAsync(
                slot,
                checkpointSaveReason,
                deadline,
                cancellationToken)
            .ConfigureAwait(false);
        slot.StopFailureReason = null;
        CompleteTaskStop(slot.TaskKey, disposition);
    }

    private async Task SaveTaskCheckpointIfRequiredAsync(
        BusinessTaskSlot slot,
        PlcTaskCheckpointSaveReason? saveReason,
        StopDeadline deadline,
        CancellationToken cancellationToken)
    {
        if (saveReason is null
            || slot.Task is not IPlcTaskCheckpointParticipant participant)
        {
            return;
        }

        var identity = participant.CheckpointIdentity;
        if (!string.Equals(identity.ModuleId, slot.ModuleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identity.PlcCode, PlcCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identity.TaskKey, slot.TaskKey, StringComparison.OrdinalIgnoreCase))
        {
            slot.StopFailureReason = PlcTaskRuntimeErrorCodes.TaskCheckpointSaveFailed;
            SetTaskState(
                slot.TaskKey,
                PlcTaskRuntimeState.Faulted,
                PlcTaskRuntimeErrorCodes.TaskCheckpointSaveFailed);
            throw new InvalidOperationException(
                $"业务任务 {slot.TaskKey} 检查点身份不匹配，"
                + $"原因码={PlcTaskRuntimeErrorCodes.TaskCheckpointSaveFailed}。");
        }

        PlcTaskCheckpointSaveResult result;
        try
        {
            result = await participant
                .SaveCheckpointAsync(saveReason.Value, cancellationToken)
                .AsTask()
                .WaitAsync(
                    deadline.Remaining,
                    TransitionTimeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            slot.StopFailureReason = PlcTaskRuntimeErrorCodes.TaskCheckpointSaveFailed;
            SetTaskState(
                slot.TaskKey,
                PlcTaskRuntimeState.Faulted,
                PlcTaskRuntimeErrorCodes.TaskCheckpointSaveFailed,
                ex.GetType().Name);
            throw new InvalidOperationException(
                $"业务任务 {slot.TaskKey} 检查点保存失败，"
                + $"原因码={PlcTaskRuntimeErrorCodes.TaskCheckpointSaveFailed}，"
                + $"异常类型={ex.GetType().Name}。",
                ex);
        }

        if (result.IsPersisted)
        {
            return;
        }

        slot.StopFailureReason = PlcTaskRuntimeErrorCodes.TaskCheckpointSaveFailed;
        SetTaskState(
            slot.TaskKey,
            PlcTaskRuntimeState.Faulted,
            PlcTaskRuntimeErrorCodes.TaskCheckpointSaveFailed);
        throw new InvalidOperationException(
            $"业务任务 {slot.TaskKey} 检查点未持久化，"
            + $"原因码={PlcTaskRuntimeErrorCodes.TaskCheckpointSaveFailed}。");
    }

    private async Task StopPeriodicReadTaskAsync(
        CancellationToken cancellationToken,
        StopDeadline? deadline = null)
    {
        Interlocked.Exchange(ref _periodicReadAvailable, 0);
        deadline ??= CreateStopDeadline();
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
                    .WaitAsync(
                        deadline.Remaining,
                        TransitionTimeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"PLC“{PlcCode}”周期读取停止钩子超过 {StopTimeout.TotalSeconds:0} 秒，runtime 将保留隔离。",
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
                await execution
                    .WaitAsync(
                        deadline.Remaining,
                        TransitionTimeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
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
        _shutdownStopDeadline = CreateStopDeadline();
        await CancellationTokenSource.CancelAsync().ConfigureAwait(false);
        ConnectionSignal.Complete();

        await _taskGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Interlocked.Exchange(ref _connected, 0);
            await StopDependentTasksAsync(
                    _shutdownStopDeadline,
                    CancellationToken.None,
                    BusinessTaskStopDisposition.PreserveStopping,
                    PlcTaskCheckpointSaveReason.ProcessShutdown)
                .ConfigureAwait(false);
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

    private void SetAllTaskStates(
        PlcTaskRuntimeState state,
        string? errorCode = null,
        string? exceptionType = null)
    {
        foreach (var taskKey in GetBusinessTaskKeysSnapshot())
        {
            SetTaskState(taskKey, state, errorCode, exceptionType);
        }
    }

    private void SetPeriodicReadDependentTaskStates(
        PlcTaskRuntimeState state,
        string? errorCode = null,
        string? exceptionType = null)
    {
        foreach (var slot in GetBusinessTaskSlotsSnapshot()
                     .Values
                     .Where(static slot =>
                         slot.RequiresPeriodicRead
                         && slot.StopFailureReason is null))
        {
            SetTaskState(slot.TaskKey, state, errorCode, exceptionType);
        }
    }

    private void SetTaskState(
        string taskKey,
        PlcTaskRuntimeState state,
        string? errorCode = null,
        string? exceptionType = null)
        => TaskStatusWriter?.SetState(
            PlcCode,
            taskKey,
            state,
            errorCode,
            exceptionType);

    private bool IsRuntimeQuarantined
        => Volatile.Read(ref _runtimeQuarantined) != 0;

    private bool IsPeriodicReadAvailable
        => Volatile.Read(ref _periodicReadAvailable) != 0;

    private bool CanStartBusinessTask(BusinessTaskSlot slot)
        => !slot.RequiresPeriodicRead || IsPeriodicReadAvailable;

    private void MarkRuntimeQuarantined(
        PlcServiceQuarantinedException exception)
    {
        Interlocked.Exchange(ref _runtimeQuarantined, 1);
        Interlocked.Exchange(ref _connected, 0);
        StatusStore.MarkRuntimeFault(
            DeviceId,
            PlcCode,
            DeviceName,
            PlcServiceQuarantinedException.StableReasonCode);
        SetAllTaskStates(
            PlcTaskRuntimeState.Faulted,
            PlcTaskRuntimeErrorCodes.RuntimeQuarantined,
            exception.GetType().Name);
    }

    private void SetTaskDisconnectedState(string taskKey)
    {
        if (IsRuntimeQuarantined)
        {
            SetTaskState(
                taskKey,
                PlcTaskRuntimeState.Faulted,
                PlcTaskRuntimeErrorCodes.RuntimeQuarantined,
                nameof(PlcServiceQuarantinedException));
            return;
        }

        SetTaskState(taskKey, PlcTaskRuntimeState.WaitingForConnection);
    }

    private void CompleteTaskStop(
        string taskKey,
        BusinessTaskStopDisposition disposition)
    {
        switch (disposition)
        {
            case BusinessTaskStopDisposition.Remove:
                TaskStatusWriter?.Remove(PlcCode, taskKey);
                break;
            case BusinessTaskStopDisposition.WaitingForConnection:
                SetTaskState(taskKey, PlcTaskRuntimeState.WaitingForConnection);
                break;
            case BusinessTaskStopDisposition.PreserveStopping:
            case BusinessTaskStopDisposition.PreserveState:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null);
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
                    $"PLC“{PlcCode}”runtime 中不存在业务任务 {taskKey}。");
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

    private StopDeadline CreateStopDeadline()
        => new(TransitionTimeProvider);

    private sealed record PreviousTaskState(
        BusinessTaskSlot Slot,
        bool WasRunning);

    private enum BusinessTaskStopDisposition
    {
        Remove,
        WaitingForConnection,
        PreserveStopping,
        PreserveState
    }

    private sealed class BusinessTaskSlot(
        string taskKey,
        PlcRuntimeTaskPlanEntry entry)
    {
        public string TaskKey { get; } = taskKey;

        public string ModuleId { get; } = entry.ModuleId;

        public PlcRuntimeBusinessTaskFactory Factory { get; private set; } = entry.Factory;

        public bool RequiresPeriodicRead { get; private set; } = entry.RequiresPeriodicRead;

        public IPlcTask? Task { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }

        public Task? Execution { get; set; }

        public Task? StopExecution { get; set; }

        public string? UnexpectedExitReason { get; set; }

        public string? StopFailureReason { get; set; }

        public bool IsRunning => Execution is { IsCompleted: false };

        public void UpdatePlanEntry(PlcRuntimeTaskPlanEntry next)
        {
            ArgumentNullException.ThrowIfNull(next);
            if (!string.Equals(ModuleId, next.ModuleId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"业务任务 {TaskKey} 的 ModuleId 不得在运行时变更。");
            }

            Factory = next.Factory;
            RequiresPeriodicRead = next.RequiresPeriodicRead;
        }
    }

    private sealed class StopDeadline(TimeProvider timeProvider)
    {
        private readonly long _startedAt = timeProvider.GetTimestamp();

        public TimeSpan Remaining
        {
            get
            {
                var remaining = StopTimeout - timeProvider.GetElapsedTime(_startedAt);
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }
}
