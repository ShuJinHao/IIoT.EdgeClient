namespace IIoT.Edge.Application.Abstractions.Modules;

public sealed record MesChannelDiagnostics(
    string ProcessType,
    DateTime? LastAttemptAt,
    DateTime? LastSuccessAt,
    string LastResult,
    string? LastFailureReason);

public interface IMesUploadDiagnosticsStore
{
    IReadOnlyList<MesChannelDiagnostics> GetAll();

    MesChannelDiagnostics? Get(string processType);

    void RecordSuccess(string processType);

    void RecordFailure(string processType, string failureReason);
}
