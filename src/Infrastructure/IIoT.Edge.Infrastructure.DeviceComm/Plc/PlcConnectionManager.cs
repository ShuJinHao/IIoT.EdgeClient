using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Factory;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public class PlcConnectionManager : IPlcConnectionManager
{
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly IPlcDataStore _dataStore;
    private readonly IPlcServiceFactory _plcServiceFactory;
    private readonly IProductionContextStore _contextStore;
    private readonly ILogService _logger;
    private readonly Dictionary<int, IPlcService> _plcInstances = new();
    private readonly Dictionary<int, List<IPlcTask>> _plcTasks = new();
    private readonly Dictionary<int, CancellationTokenSource> _deviceCtsMap = new();
    private readonly Dictionary<int, List<Task>> _runningTaskHandles = new();
    private readonly Dictionary<int, string> _deviceNames = new();
    private readonly Dictionary<string, Func<IPlcBuffer, ProductionContext, List<IPlcTask>>> _taskFactories = new();
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private int _shutdownRequested;
    private int _disposeState;

    public PlcConnectionManager(
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<IoMappingEntity> ioMappings,
        IPlcDataStore dataStore,
        IPlcServiceFactory plcServiceFactory,
        IProductionContextStore contextStore,
        ILogService logger)
    {
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _dataStore = dataStore;
        _plcServiceFactory = plcServiceFactory;
        _contextStore = contextStore;
        _logger = logger;
    }

    public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
    {
        lock (_stateLock)
        {
            _taskFactories[deviceName] = factory;
        }
    }

    public IPlcService? GetPlc(int networkDeviceId)
    {
        lock (_stateLock)
        {
            _plcInstances.TryGetValue(networkDeviceId, out var plc);
            return plc;
        }
    }

    public ProductionContext? GetContext(string deviceName) => _contextStore.GetOrCreate(deviceName);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var devices = await _networkDevices.GetListAsync(
                x => x.IsEnabled && x.DeviceType == DeviceType.PLC,
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

    public async Task StopAsync(CancellationToken ct = default)
    {
        RequestShutdown();

        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _contextStore.SaveToFile();
            var deviceIds = GetTrackedDeviceIdsSnapshot();
            foreach (var deviceId in deviceIds)
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

        Dictionary<int, CancellationTokenSource> deviceCtsMap;
        Dictionary<int, List<Task>> runningTaskHandles;
        Dictionary<int, IPlcService> plcInstances;
        Dictionary<int, string> deviceNames;

        lock (_stateLock)
        {
            deviceCtsMap = new Dictionary<int, CancellationTokenSource>(_deviceCtsMap);
            runningTaskHandles = new Dictionary<int, List<Task>>(_runningTaskHandles);
            plcInstances = new Dictionary<int, IPlcService>(_plcInstances);
            deviceNames = new Dictionary<int, string>(_deviceNames);

            _deviceCtsMap.Clear();
            _runningTaskHandles.Clear();
            _plcInstances.Clear();
            _plcTasks.Clear();
            _deviceNames.Clear();
        }

        foreach (var cts in deviceCtsMap.Values)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }

        _ = Task.Run(async () =>
        {
            foreach (var pair in runningTaskHandles)
            {
                var deviceName = deviceNames.TryGetValue(pair.Key, out var trackedName)
                    ? trackedName
                    : $"DeviceId={pair.Key}";
                try
                {
                    await AwaitRunningHandlesAsync(deviceName, pair.Value, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            foreach (var plc in plcInstances.Values)
            {
                try
                {
                    plc.Disconnect();
                    plc.Dispose();
                }
                catch
                {
                }
            }

            foreach (var cts in deviceCtsMap.Values)
            {
                cts.Dispose();
            }
        }, CancellationToken.None);
    }

    private async Task InitializeDeviceSafelyAsync(NetworkDeviceEntity device, CancellationToken ct)
    {
        try
        {
            await InitializeDeviceAsync(device, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{device.DeviceName}] Initialization failed: {ex.Message}");
        }
    }

    private async Task InitializeDeviceAsync(NetworkDeviceEntity device, CancellationToken ct)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (_deviceCtsMap.ContainsKey(device.Id))
            {
                _logger.Info($"[{device.DeviceName}] Skipped initialization because the device is already running.");
                return;
            }
        }

        var mappings = await _ioMappings.GetListAsync(x => x.NetworkDeviceId == device.Id, ct).ConfigureAwait(false);
        var mappingArray = mappings.OrderBy(x => x.SortOrder).ToArray();
        var readCount = mappingArray.Where(x => x.Direction == "Read").Sum(x => x.AddressCount);
        var writeCount = mappingArray.Where(x => x.Direction == "Write").Sum(x => x.AddressCount);

        _dataStore.Register(device.Id, readCount, writeCount);
        var buffer = _dataStore.GetBuffer(device.Id);
        var context = _contextStore.GetOrCreate(device.DeviceName);
        context.DeviceId = device.Id;

        if (!Enum.TryParse<PlcType>(device.DeviceModel, ignoreCase: true, out var plcType))
        {
            _logger.Error(
                $"[{device.DeviceName}] Initialization skipped because DeviceModel is invalid: {device.DeviceModel ?? "<empty>"}.");
            return;
        }

        IPlcService? plcService = null;
        CancellationTokenSource? deviceCts = null;
        var handles = new List<Task>();
        var registered = false;

        try
        {
            plcService = _plcServiceFactory.Create(plcType, device.DeviceName);
            deviceCts = new CancellationTokenSource();

            var signalInteraction = new SignalInteraction(plcService, _dataStore, device, mappingArray, _logger);
            await signalInteraction.ConnectAsync().ConfigureAwait(false);

            var tasks = new List<IPlcTask> { signalInteraction };
            Func<IPlcBuffer, ProductionContext, List<IPlcTask>>? factory = null;
            lock (_stateLock)
            {
                _taskFactories.TryGetValue(device.DeviceName, out factory);
            }

            if (buffer is not null && factory is not null)
            {
                tasks.AddRange(factory(buffer, context));
            }

            foreach (var task in tasks)
            {
                var handle = Task.Run(async () =>
                {
                    try
                    {
                        await task.StartAsync(deviceCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (deviceCts.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[{device.DeviceName}] Task failed: {ex.Message}");
                    }
                }, CancellationToken.None);

                handles.Add(handle);
            }

            lock (_stateLock)
            {
                if (IsShutdownRequested || IsDisposed)
                {
                    return;
                }

                _plcInstances[device.Id] = plcService;
                _deviceCtsMap[device.Id] = deviceCts;
                _plcTasks[device.Id] = tasks;
                _runningTaskHandles[device.Id] = handles;
                _deviceNames[device.Id] = device.DeviceName;
                registered = true;
            }

            _logger.Info($"[{device.DeviceName}] Initialized and started {tasks.Count} task(s).");
        }
        catch
        {
            if (!registered)
            {
                await CleanupDeviceResourcesAsync(device.DeviceName, deviceCts, handles, plcService, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            throw;
        }

        if (!registered)
        {
            await CleanupDeviceResourcesAsync(device.DeviceName, deviceCts, handles, plcService, CancellationToken.None)
                .ConfigureAwait(false);
            _logger.Warn($"[{device.DeviceName}] Initialization was canceled before task handles could be tracked.");
        }
    }

    private async Task StopDeviceCoreAsync(int deviceId, CancellationToken ct)
    {
        CancellationTokenSource? deviceCts = null;
        List<Task>? runningHandles = null;
        IPlcService? plc = null;
        var deviceName = $"DeviceId={deviceId}";

        lock (_stateLock)
        {
            if (_deviceCtsMap.TryGetValue(deviceId, out var cts))
            {
                deviceCts = cts;
                _deviceCtsMap.Remove(deviceId);
            }

            if (_runningTaskHandles.TryGetValue(deviceId, out var handles))
            {
                runningHandles = handles;
                _runningTaskHandles.Remove(deviceId);
            }

            _plcTasks.Remove(deviceId);

            if (_plcInstances.TryGetValue(deviceId, out var instance))
            {
                plc = instance;
                _plcInstances.Remove(deviceId);
            }

            if (_deviceNames.TryGetValue(deviceId, out var trackedDeviceName))
            {
                deviceName = trackedDeviceName;
                _deviceNames.Remove(deviceId);
            }
        }

        await CleanupDeviceResourcesAsync(deviceName, deviceCts, runningHandles, plc, ct).ConfigureAwait(false);
    }

    private async Task CleanupDeviceResourcesAsync(
        string deviceName,
        CancellationTokenSource? deviceCts,
        IReadOnlyCollection<Task>? runningHandles,
        IPlcService? plc,
        CancellationToken ct)
    {
        if (deviceCts is not null)
        {
            try
            {
                deviceCts.Cancel();
            }
            catch
            {
            }
        }

        await AwaitRunningHandlesAsync(deviceName, runningHandles, ct).ConfigureAwait(false);

        if (deviceCts is not null)
        {
            deviceCts.Dispose();
        }

        if (plc is not null)
        {
            plc.Disconnect();
            plc.Dispose();
        }
    }

    private async Task AwaitRunningHandlesAsync(
        string deviceName,
        IReadOnlyCollection<Task>? runningHandles,
        CancellationToken ct)
    {
        if (runningHandles is null || runningHandles.Count == 0)
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

    private int[] GetTrackedDeviceIdsSnapshot()
    {
        lock (_stateLock)
        {
            return _deviceCtsMap.Keys
                .Concat(_plcInstances.Keys)
                .Concat(_runningTaskHandles.Keys)
                .Distinct()
                .ToArray();
        }
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed || IsShutdownRequested)
        {
            throw new ObjectDisposedException(nameof(PlcConnectionManager));
        }
    }

    private void RequestShutdown()
    {
        Interlocked.Exchange(ref _shutdownRequested, 1);
    }

    private bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;

    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;
}
