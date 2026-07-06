using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcLifecycleCoordinator
{
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IProductionContextStore _contextStore;
    private readonly ILogService _logger;
    private readonly PlcRuntimeRegistry _runtimeRegistry;
    private readonly PlcDeviceRuntimeBuilder _runtimeBuilder;
    private readonly PlcConnectionStatusStore _statusStore;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private int _shutdownRequested;
    private int _disposeState;

    public PlcLifecycleCoordinator(
        IRepository<NetworkDeviceEntity> networkDevices,
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
                x => x.IsEnabled && x.DeviceType == SharedKernel.Enums.DeviceType.PLC,
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
                x => x.IsEnabled && x.DeviceType == SharedKernel.Enums.DeviceType.PLC,
                ct).ConfigureAwait(false);
            var duplicateEndpointFaults = DiagnoseDuplicateEnabledTcpEndpoints(enabledDevices);
            foreach (var duplicateDeviceId in duplicateEndpointFaults.Keys)
            {
                await StopDeviceCoreAsync(duplicateDeviceId, ct).ConfigureAwait(false);
            }

            if (!duplicateEndpointFaults.ContainsKey(device.Id))
            {
                await StopDeviceCoreAsync(device.Id, ct).ConfigureAwait(false);
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
            await StopDeviceCoreAsync(networkDeviceId, ct).ConfigureAwait(false);
            _logger.Info($"[DeviceId={networkDeviceId}] 停止完成。");
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
            _contextStore.SaveToFile();
            foreach (var deviceId in _runtimeRegistry.GetTrackedDeviceIdsSnapshot())
            {
                await StopDeviceCoreAsync(deviceId, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        RequestShutdown();
        _ = Task.Run(async () =>
        {
            try
            {
                await DisposeCoreAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"PLC 生命周期释放清理失败：{ex.Message}");
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        RequestShutdown();
        await DisposeCoreAsync().ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        PlcDeviceRuntimeHandle[] runtimes;

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            runtimes = _runtimeRegistry.Drain();
        }
        finally
        {
            _lifecycleGate.Release();
        }

        foreach (var runtime in runtimes)
        {
            await CleanupDeviceRuntimeAsync(runtime, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task InitializeDeviceSafelyAsync(NetworkDeviceEntity device, CancellationToken ct)
    {
        try
        {
            await InitializeDeviceAsync(device, ct).ConfigureAwait(false);
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

        if (_runtimeRegistry.IsRuntimeBlocked(device.DeviceName))
        {
            _logger.Warn($"[{device.DeviceName}] PLC 运行启动已被任务绑定校验阻断。");
            return;
        }

        if (_runtimeRegistry.ContainsRuntime(device.Id))
        {
            _logger.Info($"[{device.DeviceName}] 初始化跳过：设备已在运行。");
            return;
        }

        _statusStore.EnsureTracked(device.Id, device.DeviceName);
        var taskFactory = _runtimeRegistry.GetTaskFactory(device.DeviceName);
        PlcDeviceRuntimeHandle? runtime = null;

        try
        {
            runtime = await _runtimeBuilder.BuildAsync(device, taskFactory, ct).ConfigureAwait(false);

            foreach (var task in runtime.Tasks)
            {
                runtime.RunningHandles.Add(Task.Run(async () =>
                {
                    try
                    {
                        await task.StartAsync(runtime.CancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (runtime.CancellationTokenSource.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        _statusStore.MarkDisconnected(runtime.DeviceId, runtime.DeviceName, ex.Message);
                        _logger.Error($"[{runtime.DeviceName}] 运行任务异常：{ex.Message}");
                    }
                }, CancellationToken.None));
            }

            if (IsShutdownRequested || IsDisposed || !_runtimeRegistry.TryAddRuntime(runtime))
            {
                await CleanupDeviceRuntimeAsync(runtime, CancellationToken.None).ConfigureAwait(false);
                _logger.Warn($"[{device.DeviceName}] 初始化已取消：任务句柄尚未完成登记。");
                return;
            }

            var taskNames = runtime.Tasks
                .Select(static task => ToVisibleTaskName(task.TaskName))
                .ToArray();
            _logger.Info($"[{device.DeviceName}] 初始化完成，已启动 {runtime.Tasks.Count} 个任务：{string.Join("、", taskNames)}。");
            if (runtime.Tasks.All(static task => IsBasePlcTask(task.TaskName)))
            {
                _logger.Warn($"[{device.DeviceName}] 当前仅启动 PLC 基础任务，未挂载业务采集任务，请检查任务绑定和 IO 必需点位。");
            }
        }
        catch
        {
            if (runtime is not null)
            {
                await CleanupDeviceRuntimeAsync(runtime, CancellationToken.None).ConfigureAwait(false);
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

    private async Task StopDeviceCoreAsync(int deviceId, CancellationToken ct)
    {
        if (_runtimeRegistry.TryRemoveRuntime(deviceId, out var runtime) && runtime is not null)
        {
            await CleanupDeviceRuntimeAsync(runtime, ct).ConfigureAwait(false);
            return;
        }

        var device = (await _networkDevices.GetListAsync(x => x.Id == deviceId, ct).ConfigureAwait(false)).FirstOrDefault();
        if (device is not null)
        {
            _statusStore.MarkDisconnected(device.Id, device.DeviceName);
        }
    }

    private static bool IsBasePlcTask(string taskName)
        => taskName.StartsWith("PlcIoScan_", StringComparison.OrdinalIgnoreCase)
           || taskName.StartsWith("PlcDataReadScan_", StringComparison.OrdinalIgnoreCase);

    private static string ToVisibleTaskName(string taskName)
    {
        if (taskName.StartsWith("PlcIoScan_", StringComparison.OrdinalIgnoreCase))
        {
            return "PLC交互扫描";
        }

        if (taskName.StartsWith("PlcDataReadScan_", StringComparison.OrdinalIgnoreCase))
        {
            return "PLC只读采集";
        }

        if (taskName.Contains("RealtimeSampleUpload", StringComparison.OrdinalIgnoreCase))
        {
            return "模切采样上传";
        }

        return taskName;
    }

    private async Task CleanupDeviceRuntimeAsync(PlcDeviceRuntimeHandle runtime, CancellationToken ct)
    {
        try
        {
            runtime.CancellationTokenSource.Cancel();
        }
        catch
        {
        }

        await AwaitRunningHandlesAsync(runtime.DeviceName, runtime.RunningHandles, ct).ConfigureAwait(false);

        runtime.CancellationTokenSource.Dispose();

        try
        {
            runtime.PlcService.Disconnect();
        }
        catch
        {
        }

        runtime.PlcService.Dispose();
        _statusStore.MarkDisconnected(runtime.DeviceId, runtime.DeviceName);
    }

    private async Task AwaitRunningHandlesAsync(
        string deviceName,
        IReadOnlyCollection<Task> runningHandles,
        CancellationToken ct)
    {
        if (runningHandles.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(runningHandles).WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Warn($"[{deviceName}] 等待 PLC 任务停止超时：5 秒内未完成。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[{deviceName}] 等待 PLC 任务停止时发生异常：{ex.Message}");
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

    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;
}
