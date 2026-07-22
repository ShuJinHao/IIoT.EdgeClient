using IIoT.Edge.Module.Contracts.Modules;

namespace IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

public sealed record PlcTaskBindingDeviceDto(
    int NetworkDeviceId,
    string DeviceName,
    string ModuleId,
    bool IsDeviceEnabled,
    IReadOnlyList<PlcTaskBindingItemDto> Tasks);

public sealed record PlcTaskBindingItemDto(
    string Key,
    string DisplayName,
    bool Enabled,
    bool HasSavedBinding,
    bool IsHeartbeatLike,
    IReadOnlyList<TaskRequiredSignal> RequiredSignals,
    bool CanRun,
    string UnavailableReason,
    IReadOnlyList<TaskRequiredSignal> MissingRequiredSignals,
    bool IsSupportedByCurrentPlc);

public sealed record PlcTaskBindingValidationResult(
    bool IsValid,
    IReadOnlyList<PlcTaskBindingValidationIssue> Issues)
{
    public static PlcTaskBindingValidationResult Success()
        => new(true, []);

    public static PlcTaskBindingValidationResult Failure(IReadOnlyList<PlcTaskBindingValidationIssue> issues)
        => new(false, issues);
}

public enum PlcTaskBindingValidationIssueType
{
    MissingRequiredSignal,
    UnsupportedDeviceModel
}

public sealed record PlcTaskBindingValidationIssue(
    string TaskKey,
    string TaskDisplayName,
    TaskRequiredSignal? RequiredSignal,
    PlcTaskBindingValidationIssueType IssueType,
    string Message);
