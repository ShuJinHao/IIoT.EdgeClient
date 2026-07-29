using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
namespace IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

public interface IPlcTaskBindingService
{
    Task<IReadOnlyList<PlcTaskBindingDeviceDto>> GetModuleDeviceBindingsAsync(
        string moduleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetEnabledTaskKeysAsync(
        int networkDeviceId,
        IReadOnlyCollection<TaskCandidate> candidates,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel = null,
        CancellationToken cancellationToken = default);

    PlcTaskBindingValidationResult ValidateEnabledTasks(
        IReadOnlyCollection<TaskCandidate> candidates,
        IReadOnlySet<string> enabledTaskKeys,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel = null);
}
