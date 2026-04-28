using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcRuntimeRegistry
{
    private readonly object _stateLock = new();
    private readonly Dictionary<int, PlcDeviceRuntimeHandle> _runtimes = new();
    private readonly Dictionary<string, Func<IPlcBuffer, ProductionContext, List<IPlcTask>>> _taskFactories = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterTaskFactory(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
    {
        lock (_stateLock)
        {
            _taskFactories[deviceName] = factory;
        }
    }

    public Func<IPlcBuffer, ProductionContext, List<IPlcTask>>? GetTaskFactory(string deviceName)
    {
        lock (_stateLock)
        {
            return _taskFactories.TryGetValue(deviceName, out var factory)
                ? factory
                : null;
        }
    }

    public bool ContainsRuntime(int deviceId)
    {
        lock (_stateLock)
        {
            return _runtimes.ContainsKey(deviceId);
        }
    }

    public bool TryAddRuntime(PlcDeviceRuntimeHandle runtime)
    {
        lock (_stateLock)
        {
            if (_runtimes.ContainsKey(runtime.DeviceId))
            {
                return false;
            }

            _runtimes[runtime.DeviceId] = runtime;
            return true;
        }
    }

    public bool TryRemoveRuntime(int deviceId, out PlcDeviceRuntimeHandle? runtime)
    {
        lock (_stateLock)
        {
            if (_runtimes.TryGetValue(deviceId, out runtime))
            {
                _runtimes.Remove(deviceId);
                return true;
            }

            runtime = null;
            return false;
        }
    }

    public PlcDeviceRuntimeHandle? GetRuntime(int deviceId)
    {
        lock (_stateLock)
        {
            return _runtimes.TryGetValue(deviceId, out var runtime)
                ? runtime
                : null;
        }
    }

    public IPlcService? GetPlc(int deviceId) => GetRuntime(deviceId)?.PlcService;

    public int[] GetTrackedDeviceIdsSnapshot()
    {
        lock (_stateLock)
        {
            return _runtimes.Keys.ToArray();
        }
    }

    public PlcDeviceRuntimeHandle[] Drain()
    {
        lock (_stateLock)
        {
            var snapshot = _runtimes.Values.ToArray();
            _runtimes.Clear();
            return snapshot;
        }
    }
}
