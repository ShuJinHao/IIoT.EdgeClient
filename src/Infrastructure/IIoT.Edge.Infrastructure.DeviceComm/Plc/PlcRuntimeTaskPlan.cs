using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Runtime;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public delegate IPlcTask PlcRuntimeBusinessTaskFactory(
    IPlcBuffer buffer,
    ProductionContext context);

public sealed class PlcRuntimeTaskPlanEntry
{
    public PlcRuntimeTaskPlanEntry(
        string moduleId,
        PlcRuntimeBusinessTaskFactory factory,
        bool requiresPeriodicRead)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(factory);
        ModuleId = moduleId.Trim();
        Factory = factory;
        RequiresPeriodicRead = requiresPeriodicRead;
    }

    public string ModuleId { get; }

    public PlcRuntimeBusinessTaskFactory Factory { get; }

    public bool RequiresPeriodicRead { get; }
}

public sealed class PlcRuntimeTaskPlan
{
    private readonly IReadOnlyDictionary<string, PlcRuntimeTaskPlanEntry> _taskEntries;

    public PlcRuntimeTaskPlan(
        int networkDeviceId,
        string plcCode,
        string deviceName,
        IEnumerable<KeyValuePair<string, PlcRuntimeTaskPlanEntry>> taskEntries)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException(
                "PLC 业务任务计划必须绑定有效的 NetworkDeviceId。",
                nameof(networkDeviceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(plcCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentNullException.ThrowIfNull(taskEntries);

        NetworkDeviceId = networkDeviceId;
        PlcCode = plcCode.Trim();
        DeviceName = deviceName.Trim();
        var normalized = new Dictionary<string, PlcRuntimeTaskPlanEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in taskEntries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);

            var taskKey = pair.Key.Trim();
            if (!normalized.TryAdd(taskKey, pair.Value))
            {
                throw new InvalidOperationException(
                    $"PLC“{PlcCode}”业务任务计划包含重复 TaskKey：{taskKey}。");
            }
        }

        _taskEntries = normalized;
    }

    public int NetworkDeviceId { get; }

    public string PlcCode { get; }

    public string DeviceName { get; }

    public IReadOnlyCollection<string> TaskKeys => _taskEntries.Keys.ToArray();

    public static PlcRuntimeTaskPlan Empty(
        int networkDeviceId,
        string plcCode,
        string deviceName)
        => new(
            networkDeviceId,
            plcCode,
            deviceName,
            Array.Empty<KeyValuePair<string, PlcRuntimeTaskPlanEntry>>());

    public PlcRuntimeTaskPlanEntry GetRequiredEntry(string taskKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        return _taskEntries.TryGetValue(taskKey, out var entry)
            ? entry
            : throw new InvalidOperationException(
                $"PLC“{PlcCode}”业务任务计划中不存在 TaskKey：{taskKey}。");
    }
}

public enum PlcRuntimeTaskApplyState
{
    Applied,
    WaitingForConnection,
    WaitingForRuntime
}

public sealed record PlcRuntimeTaskApplyResult(
    PlcRuntimeTaskApplyState State,
    IReadOnlyCollection<string> EnabledTaskKeys);
