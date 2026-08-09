using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Plc.Checkpoints;
using IIoT.Edge.Module.Sdk.Hardware;
namespace IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

public interface IPlcTaskBindingService
{
    Task<IReadOnlyList<PlcTaskBindingDeviceDto>> GetModuleDeviceBindingsAsync(
        string moduleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 监控热路径专用：只投影已经发布的插件配置与进程内运行状态，禁止查询任何 SQLite provider。
    /// </summary>
    Task<IReadOnlyList<PlcTaskBindingDeviceDto>> GetModuleDeviceBindingsFromMemoryAsync(
        string moduleId,
        CancellationToken cancellationToken = default)
        => GetModuleDeviceBindingsAsync(moduleId, cancellationToken);

    Task<IReadOnlySet<string>> GetEnabledTaskKeysAsync(
        int networkDeviceId,
        IReadOnlyCollection<TaskCandidate> candidates,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetConfiguredEnabledTaskKeysAsync(
        int networkDeviceId,
        IReadOnlyCollection<TaskCandidate> candidates,
        CancellationToken cancellationToken = default);

    PlcTaskBindingValidationResult ValidateEnabledTasks(
        IReadOnlyCollection<TaskCandidate> candidates,
        IReadOnlySet<string> enabledTaskKeys,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel = null);

    Task<PlcTaskRecoveryConfirmationResult> ConfirmRecoveryAsync(
        string moduleId,
        string plcCode,
        string taskKey,
        long expectedRevision,
        PlcTaskRecoveryConfirmationAction action,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PlcTaskRecoveryConfirmationResult.Rejected(
            PlcTaskRecoveryConfirmationOutcome.NotFound,
            PlcTaskRecoveryDiagnosticCodes.ProviderUnavailable));
}
