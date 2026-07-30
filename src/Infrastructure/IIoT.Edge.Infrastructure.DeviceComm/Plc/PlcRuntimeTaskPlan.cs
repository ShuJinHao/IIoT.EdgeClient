using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Runtime;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public delegate IPlcTask PlcRuntimeBusinessTaskFactory(
    IPlcBuffer buffer,
    ProductionContext context);

public sealed class PlcRuntimeTaskPlan
{
    private readonly IReadOnlyDictionary<string, PlcRuntimeBusinessTaskFactory> _taskFactories;

    public PlcRuntimeTaskPlan(
        int networkDeviceId,
        string plcCode,
        string deviceName,
        IEnumerable<KeyValuePair<string, PlcRuntimeBusinessTaskFactory>> taskFactories)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException(
                "PLC 业务任务计划必须绑定有效的 NetworkDeviceId。",
                nameof(networkDeviceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(plcCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentNullException.ThrowIfNull(taskFactories);

        NetworkDeviceId = networkDeviceId;
        PlcCode = plcCode.Trim();
        DeviceName = deviceName.Trim();
        var normalized = new Dictionary<string, PlcRuntimeBusinessTaskFactory>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in taskFactories)
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

        _taskFactories = normalized;
    }

    public int NetworkDeviceId { get; }

    public string PlcCode { get; }

    public string DeviceName { get; }

    public IReadOnlyCollection<string> TaskKeys => _taskFactories.Keys.ToArray();

    public static PlcRuntimeTaskPlan Empty(
        int networkDeviceId,
        string plcCode,
        string deviceName)
        => new(
            networkDeviceId,
            plcCode,
            deviceName,
            Array.Empty<KeyValuePair<string, PlcRuntimeBusinessTaskFactory>>());

    public PlcRuntimeBusinessTaskFactory GetRequiredFactory(string taskKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        return _taskFactories.TryGetValue(taskKey, out var factory)
            ? factory
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
