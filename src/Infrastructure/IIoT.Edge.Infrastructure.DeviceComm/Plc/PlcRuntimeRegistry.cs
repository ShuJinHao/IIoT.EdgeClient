using IIoT.Edge.Module.Contracts.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcRuntimeRegistry
{
    private readonly object _stateLock = new();
    private readonly Dictionary<int, PlcDeviceRuntimeHandle> _runtimes = new();
    private readonly Dictionary<string, PlcRuntimeTaskPlan> _taskPlans =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _runtimeMutationGates =
        new(StringComparer.OrdinalIgnoreCase);

    internal async ValueTask<IDisposable> EnterRuntimeMutationAsync(
        string deviceName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        SemaphoreSlim gate;
        lock (_stateLock)
        {
            if (!_runtimeMutationGates.TryGetValue(deviceName, out gate!))
            {
                gate = new SemaphoreSlim(1, 1);
                _runtimeMutationGates.Add(deviceName, gate);
            }
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new RuntimeMutationLease(gate);
    }

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

    private sealed class RuntimeMutationLease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
