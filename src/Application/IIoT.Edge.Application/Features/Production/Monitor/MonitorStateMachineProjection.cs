namespace IIoT.Edge.Application.Features.Production.Monitor;

public sealed record MonitorStateMachineTaskSnapshot(
    string Key,
    string DisplayName,
    bool Enabled,
    bool CanRun,
    bool HasSavedBinding,
    int? StepValue,
    string StepText,
    string UnavailableReason,
    bool IsHeartbeatLike,
    int RequiredSignalCount,
    int MissingRequiredSignalCount,
    string MissingRequiredSignalsSummary);
