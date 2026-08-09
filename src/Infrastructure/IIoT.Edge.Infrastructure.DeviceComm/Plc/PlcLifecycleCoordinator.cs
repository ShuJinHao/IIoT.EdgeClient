using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcLifecycleCoordinator
{
    private readonly IDevicePluginConfigurationSnapshotAccessor _snapshots;
    private readonly ILogService _logger;
    private readonly PlcRuntimeRegistry _runtimeRegistry;
    private readonly PlcDeviceRuntimeBuilder _runtimeBuilder;
    private readonly PlcConnectionStatusStore _statusStore;
    private readonly IPlcTaskRuntimeStatusWriter? _taskStatusWriter;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _disposeLock = new();
    private int _shutdownRequested;
    private Task? _disposeTask;

    public PlcLifecycleCoordinator(
        IDevicePluginConfigurationSnapshotAccessor snapshots,
        ILogService logger,
        PlcRuntimeRegistry runtimeRegistry,
        PlcDeviceRuntimeBuilder runtimeBuilder,
        PlcConnectionStatusStore statusStore,
        IPlcTaskRuntimeStatusWriter? taskStatusWriter = null)
    {
        _snapshots = snapshots;
        _logger = logger;
        _runtimeRegistry = runtimeRegistry;
        _runtimeBuilder = runtimeBuilder;
        _statusStore = statusStore;
        _taskStatusWriter = taskStatusWriter;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            ct.ThrowIfCancellationRequested();
            var devices = _snapshots.GetPlcs()
                .Where(static item => item.IsEnabled)
                .ToArray();

            var duplicateEndpointFaults = DiagnoseDuplicateEnabledTcpEndpoints(devices);
            ApplyDuplicateEndpointFaults(devices, duplicateEndpointFaults);

            await Task.WhenAll(devices
                    .Where(device => !duplicateEndpointFaults.ContainsKey(device.Id))
                    .Select(device => InitializeDeviceSafelyAsync(device, ct)))
                .ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task ReloadDeviceAsync(int networkDeviceId, CancellationToken ct = default)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException(
                "网络设备 Id 必须大于 0。",
                nameof(networkDeviceId));
        }

        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            ct.ThrowIfCancellationRequested();
            var device = _snapshots.GetPlcs()
                .SingleOrDefault(item => item.Id == networkDeviceId);
            if (device is null)
            {
                _logger.Warn($"[{ResolveLogIdentity(networkDeviceId)}] 重载跳过：未找到设备。");
                return;
            }

            var enabledDevices = _snapshots.GetPlcs()
                .Where(static item => item.IsEnabled)
                .ToArray();
            var duplicateEndpointFaults = DiagnoseDuplicateEnabledTcpEndpoints(enabledDevices);
            foreach (var duplicateDeviceId in duplicateEndpointFaults.Keys)
            {
                await StopDeviceCoreAsync(duplicateDeviceId, ct).ConfigureAwait(false);
            }

            if (!duplicateEndpointFaults.ContainsKey(device.Id))
            {
                var stopped = await StopDeviceCoreAsync(device.Id, ct).ConfigureAwait(false);
                if (!stopped)
                {
                    _logger.Warn($"[PlcCode={device.PlcCode}] 重载已停止：旧 PLC runtime 未安全退出，禁止创建替代 runtime。");
                    return;
                }
            }

            ApplyDuplicateEndpointFaults(enabledDevices, duplicateEndpointFaults);
            if (!device.IsEnabled)
            {
                _logger.Info($"[PlcCode={device.PlcCode}] 重载完成：设备未启用。");
                return;
            }

            if (duplicateEndpointFaults.ContainsKey(device.Id))
            {
                _logger.Warn($"[PlcCode={device.PlcCode}] 重载完成：PLC 端点重复，运行任务已暂停。");
                return;
            }

            await InitializeDeviceAsync(device, ct).ConfigureAwait(false);
            _logger.Info($"[PlcCode={device.PlcCode}] 重载完成，运行上下文已保留。");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var stopped = await StopDeviceCoreAsync(networkDeviceId, ct).ConfigureAwait(false);
            if (stopped)
            {
                _logger.Info($"[{ResolveLogIdentity(networkDeviceId)}] 停止完成。");
            }
            else
            {
                _logger.Warn($"[{ResolveLogIdentity(networkDeviceId)}] PLC runtime 未在硬上限内退出，已隔离但未阻断客户端。");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        RequestShutdown();

        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<Exception>? failures = null;
            foreach (var deviceId in _runtimeRegistry.GetTrackedDeviceIdsSnapshot())
            {
                try
                {
                    await StopDeviceCoreAsync(deviceId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                    _logger.Error(
                        $"[{ResolveLogIdentity(deviceId)}] 停止失败，异常类型={ex.GetType().Name}。");
                }
            }

            if (failures is { Count: 1 })
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();

            if (failures is { Count: > 1 })
                throw new AggregateException(failures);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Dispose()
    {
        RequestShutdown();
        _ = ObserveDisposeAsync(EnsureDisposeTask());
    }

    public async ValueTask DisposeAsync()
    {
        RequestShutdown();
        await EnsureDisposeTask().ConfigureAwait(false);
    }

    private Task EnsureDisposeTask()
    {
        lock (_disposeLock)
        {
            return _disposeTask ??= DisposeCoreAsync();
        }
    }

    private async Task ObserveDisposeAsync(Task disposeTask)
    {
        try
        {
            await disposeTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"PLC 生命周期释放清理失败，异常类型={ex.GetType().Name}。");
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var deviceId in _runtimeRegistry.GetTrackedDeviceIdsSnapshot())
            {
                await StopDeviceCoreAsync(deviceId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task InitializeDeviceSafelyAsync(DevicePluginPlcSnapshot device, CancellationToken ct)
    {
        try
        {
            await InitializeDeviceAsync(device, ct).ConfigureAwait(false);
        }
        catch (PlcServiceQuarantinedException ex)
        {
            _statusStore.MarkRuntimeFault(
                device.Id,
                device.PlcCode,
                device.DeviceName,
                PlcServiceQuarantinedException.StableReasonCode);
            SetTaskPlanState(
                device,
                PlcTaskRuntimeState.Faulted,
                PlcTaskRuntimeErrorCodes.RuntimeQuarantined,
                ex.GetType().Name);
            _logger.Error(
                $"[PlcCode={device.PlcCode}] PLC service 已隔离，客户端继续启动，"
                + $"原因码={PlcServiceQuarantinedException.StableReasonCode}，异常类型={ex.GetType().Name}。");
        }
        catch (Exception ex) when (
            PlcOperationFailureClassifier.IsCallerCancellation(ex, ct))
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = PlcOperationFailureClassifier.Classify(ex);
            if (failure.DisconnectsTransport)
            {
                _statusStore.MarkDisconnected(
                    device.Id,
                    device.PlcCode,
                    device.DeviceName,
                    failure.ReasonCode);
                SetTaskPlanState(
                    device,
                    PlcTaskRuntimeState.WaitingForConnection);
            }
            else
            {
                _statusStore.MarkRuntimeFault(
                    device.Id,
                    device.PlcCode,
                    device.DeviceName,
                    PlcTaskRuntimeErrorCodes.RuntimeInitializationFailed);
                SetTaskPlanState(
                    device,
                    PlcTaskRuntimeState.Faulted,
                    failure.ReasonCode,
                    failure.ExceptionType);
            }

            _logger.Error(
                $"[PlcCode={device.PlcCode}] 初始化失败，{failure.SafeDiagnostic}。");
        }
    }

    private async Task InitializeDeviceAsync(DevicePluginPlcSnapshot device, CancellationToken ct)
    {
        ThrowIfDisposed();
        using var mutation = await _runtimeRegistry
            .EnterRuntimeMutationAsync(device.Id, ct)
            .ConfigureAwait(false);

        if (_runtimeRegistry.ContainsRuntime(device.Id))
        {
            _logger.Info($"[PlcCode={device.PlcCode}] 初始化跳过：设备已在运行。");
            return;
        }

        _statusStore.EnsureTracked(device.Id, device.PlcCode, device.DeviceName);
        var taskPlan = _runtimeRegistry.GetTaskPlan(device.Id, device.PlcCode, device.DeviceName);
        PlcDeviceRuntimeHandle? runtime = null;
        var registered = false;

        try
        {
            runtime = await _runtimeBuilder.BuildAsync(device, taskPlan, ct).ConfigureAwait(false);
            if (IsShutdownRequested || IsDisposed || !_runtimeRegistry.TryAddRuntime(runtime))
            {
                var cleanup = await CleanupDeviceRuntimeAsync(runtime).ConfigureAwait(false);
                if (cleanup.SafelyReleased)
                {
                    _taskStatusWriter?.RemoveAll(runtime.PlcCode);
                }

                _logger.Warn($"[PlcCode={device.PlcCode}] 初始化已取消：任务句柄尚未完成登记。");
                return;
            }

            registered = true;
            runtime.Start();
            _logger.Info(
                $"[PlcCode={device.PlcCode}] 初始化完成：仅连接/重试任务已启动；周期读取和 {taskPlan.TaskKeys.Count} 个业务任务等待 TCP 成功后恢复。");
            if (taskPlan.TaskKeys.Count == 0)
            {
                _logger.Warn($"[PlcCode={device.PlcCode}] 当前没有已启用且通过校验的业务任务，请检查任务绑定和 IO 必需点位。");
            }
        }
        catch (Exception initializationFailure)
        {
            Exception? cleanupStopFailure = null;
            if (runtime is not null)
            {
                var cleanup = await CleanupDeviceRuntimeAsync(runtime).ConfigureAwait(false);
                cleanupStopFailure = cleanup.StopFailure;
                if (registered)
                {
                    if (cleanup.SafelyReleased
                        && _runtimeRegistry.TryRemoveRuntime(runtime.DeviceId, runtime))
                    {
                        _taskStatusWriter?.RemoveAll(runtime.PlcCode);
                    }
                }
                else if (cleanup.SafelyReleased)
                {
                    _taskStatusWriter?.RemoveAll(runtime.PlcCode);
                }
            }

            if (cleanupStopFailure is not null
                && !ReferenceEquals(initializationFailure, cleanupStopFailure))
            {
                throw new AggregateException(
                    "PLC runtime 初始化失败，且清理期间任务停止或检查点保存失败。",
                    initializationFailure,
                    cleanupStopFailure);
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(initializationFailure)
                .Throw();
        }
    }

    private Dictionary<int, string> DiagnoseDuplicateEnabledTcpEndpoints(IReadOnlyCollection<DevicePluginPlcSnapshot> devices)
    {
        var faults = new Dictionary<int, string>();
        foreach (var group in devices
                     .Where(static device => !string.IsNullOrWhiteSpace(device.IpAddress) && device.Port1 > 0)
                     .GroupBy(static device => $"{device.IpAddress.Trim()}:{device.Port1}", StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            var deviceIdentities = string.Join(
                "、",
                group.Select(static device => $"{device.PlcCode}({device.DeviceName})"));
            var message = $"多个已启用 PLC 指向同一端点 {group.Key}：{deviceIdentities}。已暂停这些 PLC 的运行任务，请修正为唯一端点或停用重复配置，避免多个 service 并发访问同一物理 PLC。";
            _logger.Warn($"[PLC配置][诊断] {message}");
            foreach (var device in group)
            {
                faults[device.Id] = message;
            }
        }

        return faults;
    }

    private void ApplyDuplicateEndpointFaults(
        IReadOnlyCollection<DevicePluginPlcSnapshot> devices,
        IReadOnlyDictionary<int, string> faults)
    {
        if (faults.Count == 0)
        {
            return;
        }

        foreach (var device in devices)
        {
            if (!faults.TryGetValue(device.Id, out var message))
            {
                continue;
            }

            _statusStore.MarkRuntimeFault(device.Id, device.PlcCode, device.DeviceName, message);
            SetTaskPlanState(
                device,
                PlcTaskRuntimeState.Faulted,
                PlcTaskRuntimeErrorCodes.ConfigurationInvalid);
        }
    }

    private async Task<bool> StopDeviceCoreAsync(int deviceId, CancellationToken ct)
    {
        using var mutation = await _runtimeRegistry
            .EnterRuntimeMutationAsync(deviceId, ct)
            .ConfigureAwait(false);

        var runtime = _runtimeRegistry.GetRuntime(deviceId);
        if (runtime is not null)
        {
            var stopped = await CleanupRegisteredRuntimeAsync(runtime).ConfigureAwait(false);
            if (stopped)
            {
                await FinalizeStoppedDeviceStateAsync(deviceId, ct).ConfigureAwait(false);
            }

            return stopped;
        }

        await FinalizeStoppedDeviceStateAsync(deviceId, ct).ConfigureAwait(false);
        return true;
    }

    private Task FinalizeStoppedDeviceStateAsync(
        int deviceId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var device = _snapshots.GetPlcs().SingleOrDefault(item => item.Id == deviceId);
        if (device is not null)
        {
            _statusStore.MarkDisconnected(device.Id, device.PlcCode, device.DeviceName);
            _taskStatusWriter?.RemoveAll(device.PlcCode);
            return Task.CompletedTask;
        }

        var deletedPlan = _runtimeRegistry.RemoveTaskPlan(deviceId);
        if (deletedPlan is not null)
        {
            _taskStatusWriter?.RemoveAll(deletedPlan.PlcCode);
        }

        return Task.CompletedTask;
    }

    private async Task<bool> CleanupRegisteredRuntimeAsync(PlcDeviceRuntimeHandle runtime)
    {
        var cleanup = await CleanupDeviceRuntimeAsync(runtime).ConfigureAwait(false);
        if (!cleanup.SafelyReleased)
        {
            ThrowCleanupStopFailure(cleanup.StopFailure);
            return false;
        }

        if (_runtimeRegistry.TryRemoveRuntime(runtime.DeviceId, runtime))
        {
            _taskStatusWriter?.RemoveAll(runtime.PlcCode);
            ThrowCleanupStopFailure(cleanup.StopFailure);
            return true;
        }

        const string reason = "PLC runtime 已释放，但 registry reservation 不再指向原 runtime，禁止继续自动替换。";
        _statusStore.MarkRuntimeFault(runtime.DeviceId, runtime.PlcCode, runtime.DeviceName, reason);
        SetRuntimeTaskState(
            runtime,
            PlcTaskRuntimeState.Faulted,
            PlcTaskRuntimeErrorCodes.RuntimeQuarantined);
        _logger.Error($"[PlcCode={runtime.PlcCode}] {reason}");
        ThrowCleanupStopFailure(cleanup.StopFailure);
        return false;
    }

    private async Task<RuntimeCleanupResult> CleanupDeviceRuntimeAsync(
        PlcDeviceRuntimeHandle runtime)
    {
        Exception? stopFailure = null;
        try
        {
            await runtime.RequestStopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var failure = PlcOperationFailureClassifier.Classify(ex);
            SetRuntimeTaskState(
                runtime,
                PlcTaskRuntimeState.Faulted,
                PlcTaskRuntimeErrorCodes.TaskStopFailed,
                failure.ExceptionType);
            _logger.Warn(
                $"[PlcCode={runtime.PlcCode}] 取消 PLC 运行任务时发生异常，{failure.SafeDiagnostic}。");
            stopFailure = ex;
        }

        var runningHandlesStopped = await AwaitRunningHandlesAsync(
                runtime,
                runtime.GetRunningHandlesSnapshot(),
                CancellationToken.None)
            .ConfigureAwait(false);

        if (!runningHandlesStopped)
        {
            RetainQuarantinedRuntime(
                runtime,
                "PLC 运行任务未在 5 秒硬上限内退出，禁止释放 PLC service 或创建替代 runtime。");
            return new RuntimeCleanupResult(
                SafelyReleased: false,
                StopFailure: stopFailure);
        }

        runtime.DisposeCancellation();
        try
        {
            await runtime.PlcService.DisposeAsync().ConfigureAwait(false);
        }
        catch (PlcServiceQuarantinedException ex)
        {
            RetainQuarantinedRuntime(
                runtime,
                PlcServiceQuarantinedException.StableReasonCode,
                ex.GetType().Name);
            return new RuntimeCleanupResult(
                SafelyReleased: false,
                StopFailure: stopFailure);
        }
        catch (Exception ex)
        {
            var failure = PlcOperationFailureClassifier.Classify(ex);
            RetainQuarantinedRuntime(
                runtime,
                "PLC service 释放失败，禁止创建替代 runtime。",
                failure.ExceptionType);
            return new RuntimeCleanupResult(
                SafelyReleased: false,
                StopFailure: stopFailure);
        }

        _statusStore.MarkDisconnected(runtime.DeviceId, runtime.PlcCode, runtime.DeviceName);
        return new RuntimeCleanupResult(
            SafelyReleased: true,
            StopFailure: stopFailure);
    }

    private static void ThrowCleanupStopFailure(Exception? failure)
    {
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }
    }

    private void RetainQuarantinedRuntime(
        PlcDeviceRuntimeHandle runtime,
        string reason,
        string? exceptionType = null)
    {
        var retained = _runtimeRegistry.TryAddRuntime(runtime)
                       || ReferenceEquals(_runtimeRegistry.GetRuntime(runtime.DeviceId), runtime);
        var diagnostic = retained
            ? reason
            : $"{reason} 隔离 runtime 未能重新登记，但同 DeviceId 已有 runtime 占位。";
        _statusStore.MarkRuntimeFault(runtime.DeviceId, runtime.PlcCode, runtime.DeviceName, diagnostic);
        SetRuntimeTaskState(
            runtime,
            PlcTaskRuntimeState.Faulted,
            PlcTaskRuntimeErrorCodes.RuntimeQuarantined,
            exceptionType);
        _logger.Error($"[PlcCode={runtime.PlcCode}] PLC runtime 已隔离：{diagnostic}");
    }

    private async Task<bool> AwaitRunningHandlesAsync(
        PlcDeviceRuntimeHandle runtime,
        IReadOnlyCollection<Task> runningHandles,
        CancellationToken ct)
    {
        if (runningHandles.Count == 0)
        {
            return true;
        }

        var completion = Task.WhenAll(runningHandles);
        try
        {
            await completion
                .WaitAsync(
                    runtime.GetRemainingShutdownTimeout(),
                    runtime.TransitionTimeProvider,
                    ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) when (!completion.IsCompleted)
        {
            _logger.Warn($"[PlcCode={runtime.PlcCode}] 等待 PLC 任务停止超时：共享的 5 秒停止期限内未完成。");
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = PlcOperationFailureClassifier.Classify(ex);
            _logger.Error(
                $"[PlcCode={runtime.PlcCode}] 等待 PLC 任务停止时发生异常，{failure.SafeDiagnostic}。");
            return true;
        }
    }

    private void SetTaskPlanState(
        DevicePluginPlcSnapshot device,
        PlcTaskRuntimeState state,
        string? errorCode = null,
        string? exceptionType = null)
    {
        var plan = _runtimeRegistry.GetTaskPlan(
            device.Id,
            device.PlcCode,
            device.DeviceName);
        foreach (var taskKey in plan.TaskKeys)
        {
            _taskStatusWriter?.SetState(
                device.PlcCode,
                taskKey,
                state,
                errorCode,
                exceptionType);
        }
    }

    private void SetRuntimeTaskState(
        PlcDeviceRuntimeHandle runtime,
        PlcTaskRuntimeState state,
        string? errorCode = null,
        string? exceptionType = null)
    {
        var taskKeys = runtime.EnabledTaskKeys
            .Concat(_runtimeRegistry.GetTaskPlan(
                runtime.DeviceId,
                runtime.PlcCode,
                runtime.DeviceName).TaskKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var taskKey in taskKeys)
        {
            _taskStatusWriter?.SetState(
                runtime.PlcCode,
                taskKey,
                state,
                errorCode,
                exceptionType);
        }
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed || IsShutdownRequested)
        {
            throw new ObjectDisposedException(nameof(PlcLifecycleCoordinator));
        }
    }

    private void RequestShutdown()
    {
        Interlocked.Exchange(ref _shutdownRequested, 1);
    }

    private string ResolveLogIdentity(int networkDeviceId)
    {
        var plcCode = _runtimeRegistry.GetRuntime(networkDeviceId)?.PlcCode;
        if (string.IsNullOrWhiteSpace(plcCode))
        {
            plcCode = _statusStore.GetSnapshot(networkDeviceId)?.PlcCode;
        }

        return string.IsNullOrWhiteSpace(plcCode)
            ? $"DeviceId={networkDeviceId}"
            : $"PlcCode={plcCode.Trim()}";
    }

    private bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;

    private bool IsDisposed => Volatile.Read(ref _disposeTask) is not null;

    private readonly record struct RuntimeCleanupResult(
        bool SafelyReleased,
        Exception? StopFailure);
}
