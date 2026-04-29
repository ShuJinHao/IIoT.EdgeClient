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

            foreach (var device in devices)
            {
                await InitializeDeviceSafelyAsync(device, ct).ConfigureAwait(false);
            }
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
                _logger.Warn($"[{deviceName}] Reload skipped because the device was not found.");
                return;
            }

            await StopDeviceCoreAsync(device.Id, ct).ConfigureAwait(false);
            if (!device.IsEnabled)
            {
                _logger.Info($"[{device.DeviceName}] Reload finished: device is disabled.");
                return;
            }

            await InitializeDeviceAsync(device, ct).ConfigureAwait(false);
            _logger.Info($"[{device.DeviceName}] Reload completed and context was preserved.");
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
            _logger.Info($"[DeviceId={networkDeviceId}] Stop completed.");
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
                _logger.Error($"PLC lifecycle dispose cleanup failed: {ex.Message}");
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
            _logger.Error($"[{device.DeviceName}] Initialization failed: {ex.Message}");
        }
    }

    private async Task InitializeDeviceAsync(NetworkDeviceEntity device, CancellationToken ct)
    {
        ThrowIfDisposed();

        if (_runtimeRegistry.ContainsRuntime(device.Id))
        {
            _logger.Info($"[{device.DeviceName}] Skipped initialization because the device is already running.");
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
                        _logger.Error($"[{runtime.DeviceName}] Task failed: {ex.Message}");
                    }
                }, CancellationToken.None));
            }

            if (IsShutdownRequested || IsDisposed || !_runtimeRegistry.TryAddRuntime(runtime))
            {
                await CleanupDeviceRuntimeAsync(runtime, CancellationToken.None).ConfigureAwait(false);
                _logger.Warn($"[{device.DeviceName}] Initialization was canceled before task handles could be tracked.");
                return;
            }

            _logger.Info($"[{device.DeviceName}] Initialized and started {runtime.Tasks.Count} task(s).");
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
            _logger.Warn($"[{deviceName}] Timed out waiting for PLC tasks to stop within 5 seconds.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[{deviceName}] Error while waiting for PLC tasks to stop: {ex.Message}");
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
