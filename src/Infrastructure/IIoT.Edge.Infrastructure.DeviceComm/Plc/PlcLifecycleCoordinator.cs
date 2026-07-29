using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcLifecycleCoordinator
{
    private readonly IReadRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IProductionContextStore _contextStore;
    private readonly ILogService _logger;
    private readonly PlcRuntimeRegistry _runtimeRegistry;
    private readonly PlcDeviceRuntimeBuilder _runtimeBuilder;
    private readonly PlcConnectionStatusStore _statusStore;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _disposeLock = new();
    private int _shutdownRequested;
    private Task? _disposeTask;

    public PlcLifecycleCoordinator(
        IReadRepository<NetworkDeviceEntity> networkDevices,
        IProductionContextStore contextStore,
        ILogService logger,
        PlcRuntimeRegistry runtimeRegistry,
        PlcDeviceRuntimeBuilder runtimeBuilder,
        PlcConnectionStatusStore statusStore)
    {
        _networkDevices = networkDevices;
        _contextStore = contextStore;
        _logger = logger;
        _runtimeRegistry = runtimeRegistry;
        _runtimeBuilder = runtimeBuilder;
        _statusStore = statusStore;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var devices = await _networkDevices.GetListAsync(
                x => x.IsEnabled && x.DeviceType == DeviceType.PLC,
                ct).ConfigureAwait(false);

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

    public async Task ReloadAsync(string deviceName, CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var device = (await _networkDevices.GetListAsync(x => x.DeviceName == deviceName, ct).ConfigureAwait(false))
                .FirstOrDefault();
            if (device is null)
            {
                _logger.Warn($"[{deviceName}] 重载跳过：未找到设备。");
                return;
            }

            var enabledDevices = await _networkDevices.GetListAsync(
                x => x.IsEnabled && x.DeviceType == DeviceType.PLC,
                ct).ConfigureAwait(false);
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
                    _logger.Warn($"[{device.DeviceName}] 重载已停止：旧 PLC runtime 未安全退出，禁止创建替代 runtime。");
                    return;
                }
            }

            ApplyDuplicateEndpointFaults(enabledDevices, duplicateEndpointFaults);
            if (!device.IsEnabled)
            {
                _logger.Info($"[{device.DeviceName}] 重载完成：设备未启用。");
                return;
            }

            if (duplicateEndpointFaults.ContainsKey(device.Id))
            {
                _logger.Warn($"[{device.DeviceName}] 重载完成：PLC 端点重复，运行任务已暂停。");
                return;
            }

            await InitializeDeviceAsync(device, ct).ConfigureAwait(false);
            _logger.Info($"[{device.DeviceName}] 重载完成，运行上下文已保留。");
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
                _logger.Info($"[DeviceId={networkDeviceId}] 停止完成。");
            }
            else
            {
                _logger.Warn($"[DeviceId={networkDeviceId}] PLC runtime 未在硬上限内退出，已隔离但未阻断客户端。");
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
            try
            {
                _contextStore.SaveToFile();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
                _logger.Error($"[PLC] 关闭前运行上下文保存失败：{ex.Message}");
            }

            foreach (var deviceId in _runtimeRegistry.GetTrackedDeviceIdsSnapshot())
            {
                try
                {
                    await StopDeviceCoreAsync(deviceId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                    _logger.Error($"[PLC] 停止 DeviceId={deviceId} 失败：{ex.Message}");
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
            _logger.Error($"PLC 生命周期释放清理失败：{ex.Message}");
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

    private async Task InitializeDeviceSafelyAsync(NetworkDeviceEntity device, CancellationToken ct)
    {
        try
        {
            await InitializeDeviceAsync(device, ct).ConfigureAwait(false);
        }
        catch (PlcServiceQuarantinedException ex)
        {
            _statusStore.MarkRuntimeFault(device.Id, device.DeviceName, ex.Message);
            _logger.Error($"[{device.DeviceName}] PLC service 已隔离，客户端继续启动：{ex.Message}");
        }
        catch (Exception ex)
        {
            _statusStore.MarkDisconnected(device.Id, device.DeviceName, ex.Message);
            _logger.Error($"[{device.DeviceName}] 初始化失败：{ex.Message}");
        }
    }

    private async Task InitializeDeviceAsync(NetworkDeviceEntity device, CancellationToken ct)
    {
        ThrowIfDisposed();
        using var mutation = await _runtimeRegistry
            .EnterRuntimeMutationAsync(device.Id, ct)
            .ConfigureAwait(false);

        if (_runtimeRegistry.ContainsRuntime(device.Id))
        {
            _logger.Info($"[{device.DeviceName}] 初始化跳过：设备已在运行。");
            return;
        }

        _statusStore.EnsureTracked(device.Id, device.DeviceName);
        var taskPlan = _runtimeRegistry.GetTaskPlan(device.Id, device.DeviceName);
        PlcDeviceRuntimeHandle? runtime = null;
        var registered = false;

        try
        {
            runtime = await _runtimeBuilder.BuildAsync(device, taskPlan, ct).ConfigureAwait(false);
            if (IsShutdownRequested || IsDisposed || !_runtimeRegistry.TryAddRuntime(runtime))
            {
                await CleanupDeviceRuntimeAsync(runtime).ConfigureAwait(false);
                _logger.Warn($"[{device.DeviceName}] 初始化已取消：任务句柄尚未完成登记。");
                return;
            }

            registered = true;
            runtime.Start();
            _logger.Info(
                $"[{device.DeviceName}] 初始化完成：仅连接/重试任务已启动；周期读取和 {taskPlan.TaskKeys.Count} 个业务任务等待 TCP 成功后恢复。");
            if (taskPlan.TaskKeys.Count == 0)
            {
                _logger.Warn($"[{device.DeviceName}] 当前没有已启用且通过校验的业务任务，请检查任务绑定和 IO 必需点位。");
            }
        }
        catch
        {
            if (runtime is not null)
            {
                await CleanupDeviceRuntimeAsync(runtime).ConfigureAwait(false);
                if (registered)
                {
                    _runtimeRegistry.TryRemoveRuntime(runtime.DeviceId, runtime);
                }
            }

            throw;
        }
    }

    private Dictionary<int, string> DiagnoseDuplicateEnabledTcpEndpoints(IReadOnlyCollection<NetworkDeviceEntity> devices)
    {
        var faults = new Dictionary<int, string>();
        foreach (var group in devices
                     .Where(static device => !string.IsNullOrWhiteSpace(device.IpAddress) && device.Port1 > 0)
                     .GroupBy(static device => $"{device.IpAddress.Trim()}:{device.Port1}", StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            var deviceNames = string.Join("、", group.Select(static device => device.DeviceName));
            var message = $"多个已启用 PLC 指向同一端点 {group.Key}：{deviceNames}。已暂停这些 PLC 的运行任务，请修正为唯一端点或停用重复配置，避免多个 service 并发访问同一物理 PLC。";
            _logger.Warn($"[PLC配置][诊断] {message}");
            foreach (var device in group)
            {
                faults[device.Id] = message;
            }
        }

        return faults;
    }

    private void ApplyDuplicateEndpointFaults(
        IReadOnlyCollection<NetworkDeviceEntity> devices,
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

            _statusStore.MarkRuntimeFault(device.Id, device.DeviceName, message);
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
            return await CleanupRegisteredRuntimeAsync(runtime).ConfigureAwait(false);
        }

        var device = (await _networkDevices.GetListAsync(x => x.Id == deviceId, ct).ConfigureAwait(false)).FirstOrDefault();
        if (device is not null)
        {
            _statusStore.MarkDisconnected(device.Id, device.DeviceName);
        }

        return true;
    }

    private async Task<bool> CleanupRegisteredRuntimeAsync(PlcDeviceRuntimeHandle runtime)
    {
        var cleaned = await CleanupDeviceRuntimeAsync(runtime).ConfigureAwait(false);
        if (!cleaned)
        {
            return false;
        }

        if (_runtimeRegistry.TryRemoveRuntime(runtime.DeviceId, runtime))
        {
            return true;
        }

        const string reason = "PLC runtime 已释放，但 registry reservation 不再指向原 runtime，禁止继续自动替换。";
        _statusStore.MarkRuntimeFault(runtime.DeviceId, runtime.DeviceName, reason);
        _logger.Error($"[{runtime.DeviceName}] {reason}");
        return false;
    }

    private async Task<bool> CleanupDeviceRuntimeAsync(PlcDeviceRuntimeHandle runtime)
    {
        try
        {
            await runtime.RequestStopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[{runtime.DeviceName}] 取消 PLC 运行任务时发生异常：{ex.Message}");
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
            return false;
        }

        runtime.DisposeCancellation();
        try
        {
            await runtime.PlcService.DisposeAsync().ConfigureAwait(false);
        }
        catch (PlcServiceQuarantinedException ex)
        {
            RetainQuarantinedRuntime(runtime, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            RetainQuarantinedRuntime(
                runtime,
                $"PLC service 释放失败，禁止创建替代 runtime：{ex.Message}");
            return false;
        }

        _statusStore.MarkDisconnected(runtime.DeviceId, runtime.DeviceName);
        return true;
    }

    private void RetainQuarantinedRuntime(PlcDeviceRuntimeHandle runtime, string reason)
    {
        var retained = _runtimeRegistry.TryAddRuntime(runtime)
                       || ReferenceEquals(_runtimeRegistry.GetRuntime(runtime.DeviceId), runtime);
        var diagnostic = retained
            ? reason
            : $"{reason} 隔离 runtime 未能重新登记，但同 DeviceId 已有 runtime 占位。";
        _statusStore.MarkRuntimeFault(runtime.DeviceId, runtime.DeviceName, diagnostic);
        _logger.Error($"[{runtime.DeviceName}] PLC runtime 已隔离：{diagnostic}");
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
            _logger.Warn($"[{runtime.DeviceName}] 等待 PLC 任务停止超时：共享的 5 秒停止期限内未完成。");
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[{runtime.DeviceName}] 等待 PLC 任务停止时发生异常：{ex.Message}");
            return true;
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

    private bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;

    private bool IsDisposed => Volatile.Read(ref _disposeTask) is not null;
}
