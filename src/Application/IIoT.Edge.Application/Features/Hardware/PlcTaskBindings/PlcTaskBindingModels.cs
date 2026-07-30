using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Common.Plc;

namespace IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

public sealed record PlcTaskBindingDeviceDto(
    int NetworkDeviceId,
    string DeviceName,
    string ModuleId,
    bool IsDeviceEnabled,
    IReadOnlyList<PlcTaskBindingItemDto> Tasks)
{
    public string PlcCode { get; init; } = string.Empty;
}

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
    bool IsSupportedByCurrentPlc,
    DateTimeOffset? ConfigurationStateChangedAtUtc = null,
    PlcTaskRuntimeState? RuntimeState = null,
    DateTimeOffset? RuntimeStateChangedAtUtc = null,
    string? RuntimeErrorCode = null,
    string? RuntimeExceptionType = null);

public enum PlcTaskBindingDisplayState
{
    BindingMissing,
    Disabled,
    ConfigurationInvalid,
    WaitingForRuntime,
    WaitingForConnection,
    Starting,
    Running,
    Stopping,
    Faulted
}

public static class PlcTaskBindingDisplayStateResolver
{
    public static PlcTaskBindingDisplayState Resolve(
        bool hasSavedBinding,
        bool isDeviceEnabled,
        bool isTaskEnabled,
        bool canRun,
        PlcTaskRuntimeState? runtimeState)
    {
        if (!hasSavedBinding)
        {
            return PlcTaskBindingDisplayState.BindingMissing;
        }

        if (!isDeviceEnabled || !isTaskEnabled)
        {
            return PlcTaskBindingDisplayState.Disabled;
        }

        if (!canRun)
        {
            return PlcTaskBindingDisplayState.ConfigurationInvalid;
        }

        return runtimeState switch
        {
            PlcTaskRuntimeState.WaitingForConnection => PlcTaskBindingDisplayState.WaitingForConnection,
            PlcTaskRuntimeState.Starting => PlcTaskBindingDisplayState.Starting,
            PlcTaskRuntimeState.Running => PlcTaskBindingDisplayState.Running,
            PlcTaskRuntimeState.Stopping => PlcTaskBindingDisplayState.Stopping,
            PlcTaskRuntimeState.Faulted => PlcTaskBindingDisplayState.Faulted,
            _ => PlcTaskBindingDisplayState.WaitingForRuntime
        };
    }
}

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

public enum PlcTaskBindingSaveApplyState
{
    Applied,
    WaitingForConnection,
    WaitingForRuntime
}

public sealed record PlcTaskBindingSaveApplyResult(
    PlcTaskBindingSaveApplyState State,
    IReadOnlyList<string> EnabledTaskKeys);

public sealed record PlcTaskBindingRowSnapshot(
    int Id,
    string TaskKey,
    bool Enabled,
    DateTimeOffset UpdatedAt);

public sealed record PlcTaskBindingSavePreparation(
    int NetworkDeviceId,
    string PlcCode,
    string DeviceName,
    string ModuleId,
    IReadOnlyList<string> CandidateTaskKeys,
    IReadOnlyDictionary<string, bool> ResolvedStates,
    IReadOnlyList<PlcTaskBindingRowSnapshot> OriginalRows,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> DisabledHeartbeatTaskNames);

public sealed class PlcTaskBindingTransactionException : Exception
{
    public PlcTaskBindingTransactionException(
        Exception primaryFailure,
        IReadOnlyList<Exception> rollbackFailures)
        : base(
            "PLC 任务绑定运行时应用失败，且数据库或运行时回滚未完整完成；禁止将本次操作显示为成功。",
            CreateInnerException(primaryFailure, rollbackFailures))
    {
        PrimaryFailure = primaryFailure;
        RollbackFailures = rollbackFailures.ToArray();
    }

    public Exception PrimaryFailure { get; }

    public IReadOnlyList<Exception> RollbackFailures { get; }

    private static AggregateException CreateInnerException(
        Exception primaryFailure,
        IReadOnlyList<Exception> rollbackFailures)
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        ArgumentNullException.ThrowIfNull(rollbackFailures);
        if (rollbackFailures.Count == 0)
        {
            throw new ArgumentException(
                "事务故障必须至少包含一个回滚失败。",
                nameof(rollbackFailures));
        }

        return new AggregateException([primaryFailure, .. rollbackFailures]);
    }
}
