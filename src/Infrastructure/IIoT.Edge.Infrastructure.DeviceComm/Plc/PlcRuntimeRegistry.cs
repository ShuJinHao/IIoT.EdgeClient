using IIoT.Edge.Module.Contracts.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcRuntimeRegistry
{
    private readonly object _stateLock = new();
    private readonly Dictionary<int, PlcDeviceRuntimeHandle> _runtimes = new();
    private readonly Dictionary<string, PlcRuntimeTaskPlan> _taskPlans =
        new(StringComparer.OrdinalIgnoreCase);

    public void RegisterTaskPlan(PlcRuntimeTaskPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_stateLock)
        {
            _taskPlans[plan.DeviceName] = plan;
        }
    }

    public PlcRuntimeTaskPlan GetTaskPlan(string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        lock (_stateLock)
        {
            return _taskPlans.TryGetValue(deviceName, out var plan)
                ? plan
                : PlcRuntimeTaskPlan.Empty(deviceName);
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

    public bool TryRemoveRuntime(int deviceId, PlcDeviceRuntimeHandle expectedRuntime)
    {
        lock (_stateLock)
        {
            if (_runtimes.TryGetValue(deviceId, out var currentRuntime)
                && ReferenceEquals(currentRuntime, expectedRuntime))
            {
                _runtimes.Remove(deviceId);
                return true;
            }

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

    public PlcDeviceRuntimeHandle? GetRuntime(string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        lock (_stateLock)
        {
            return _runtimes.Values.FirstOrDefault(
                runtime => string.Equals(
                    runtime.DeviceName,
                    deviceName,
                    StringComparison.OrdinalIgnoreCase));
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

    public PlcDeviceRuntimeHandle[] GetRuntimesSnapshot()
    {
        lock (_stateLock)
        {
            return _runtimes.Values.ToArray();
        }
    }
}
