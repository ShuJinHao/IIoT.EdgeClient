using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IStationRuntimeFactory
{
    string ModuleId { get; }

    IReadOnlyCollection<TaskCandidate> GetTaskCandidates();

    List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context,
        IReadOnlySet<string> enabledTaskKeys);
}

public sealed record TaskCandidate(
    string Key,
    string DisplayName,
    IReadOnlyList<TaskRequiredSignal> RequiredSignals,
    bool IsHeartbeatLike = false,
    IReadOnlyList<string>? SupportedDeviceModels = null,
    bool DefaultEnabled = false)
{
    public bool SupportsDeviceModel(string? deviceModel)
    {
        if (SupportedDeviceModels is null || SupportedDeviceModels.Count == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(deviceModel)
            && SupportedDeviceModels.Any(model => string.Equals(
                model.Trim(),
                deviceModel.Trim(),
                StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record TaskRequiredSignal(
    string SignalKey,
    string Direction);

public interface IStationRuntimeRegistry
{
    void Register(IStationRuntimeFactory factory);

    bool HasFactory(string moduleId);

    bool TryGetFactory(string moduleId, out IStationRuntimeFactory factory);

    IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations();
}
