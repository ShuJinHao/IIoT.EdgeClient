using IIoT.Edge.Module.Contracts.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcRuntimeRegistry
{
    private readonly object _stateLock = new();
    private readonly Dictionary<int, PlcDeviceRuntimeHandle> _runtimes = new();
    private readonly Dictionary<int, PlcRuntimeTaskPlan> _taskPlans = [];
    private readonly Dictionary<int, SemaphoreSlim> _runtimeMutationGates = [];

    internal async ValueTask<IDisposable> EnterRuntimeMutationAsync(
        int networkDeviceId,
        CancellationToken cancellationToken)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException(
                "PLC runtime 变更门必须绑定有效的 NetworkDeviceId。",
                nameof(networkDeviceId));
        }

        SemaphoreSlim gate;
        lock (_stateLock)
        {
            if (!_runtimeMutationGates.TryGetValue(networkDeviceId, out gate!))
            {
                gate = new SemaphoreSlim(1, 1);
                _runtimeMutationGates.Add(networkDeviceId, gate);
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
            _taskPlans[plan.NetworkDeviceId] = plan;
        }
    }

    public PlcRuntimeTaskPlan GetTaskPlan(
        int networkDeviceId,
        string deviceName)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException(
                "PLC 业务任务计划必须绑定有效的 NetworkDeviceId。",
                nameof(networkDeviceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        lock (_stateLock)
        {
            return _taskPlans.TryGetValue(networkDeviceId, out var plan)
                ? plan
                : PlcRuntimeTaskPlan.Empty(networkDeviceId, deviceName);
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
