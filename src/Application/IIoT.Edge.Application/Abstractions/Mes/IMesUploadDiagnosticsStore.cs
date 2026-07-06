namespace IIoT.Edge.Application.Abstractions.Mes;

public sealed record MesChannelDiagnostics(
    string ProcessType,
    DateTime? LastAttemptAt,
    DateTime? LastSuccessAt,
    string LastResult,
    string? LastFailureReason,
    string? ProcessDisplayName = null,
    DateTime? LastBlockedAt = null,
    string? LastBlockedReason = null,
    string? DeviceName = null,
    string? ModuleId = null,
    string? TaskKey = null,
    string? Scenario = null);

public sealed record MesUploadDiagnosticsContext(
    string? DeviceName = null,
    string? ModuleId = null,
    string? TaskKey = null,
    string? Scenario = null);

public interface IMesUploadDiagnosticsStore
{
    IReadOnlyList<MesChannelDiagnostics> GetAll();

    MesChannelDiagnostics? Get(string processType);

    void RecordSuccess(string processType, MesUploadDiagnosticsContext? context = null);

    void RecordFailure(string processType, string failureReason, MesUploadDiagnosticsContext? context = null);

    void RecordBlocked(string processType, string blockedReason, MesUploadDiagnosticsContext? context = null);
}
