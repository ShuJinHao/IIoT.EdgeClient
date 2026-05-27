using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public sealed record PluginLifecycleRow(
    string ModuleId,
    string DisplayName,
    string ProcessType,
    string Version,
    string State,
    string Message);

public sealed record ModuleRegistrationRow(
    string ModuleId,
    string ProcessType,
    string AssemblyName,
    bool IsEnabled,
    bool HasCellDataRegistration,
    bool HasRuntimeFactory,
    bool HasCloudUploader,
    bool HasMesUploader,
    bool HasHardwareProfile);

public sealed record DeviceModuleBindingRow(
    string DeviceName,
    string ModuleId,
    bool ModuleExists,
    bool ModuleEnabled,
    bool HasIoMappings);

public sealed record StartupDiagnosticIssueRow(
    string Code,
    string ModuleId,
    string DeviceName,
    string Message);

public sealed record MesChannelDiagnosticsRow(
    string ProcessType,
    string LastResult,
    string LastAttemptAt,
    string LastSuccessAt,
    string LastFailureReason);

public sealed record SyncChannelRow(
    string Channel,
    string Status,
    string Pending,
    int DeadLetterCount,
    string LastError,
    string Note);

public sealed record DeadLetterRow(
    DataPipelineRetryChannel Channel,
    long Id,
    string ProcessType,
    string FailedTarget,
    string FailureStage,
    string Source,
    string CreatedAt,
    string FailureReason,
    string CellDataJson)
{
    public static DeadLetterRow From(
        DataPipelineRetryChannel channel,
        DeadLetterRecord record,
        string processDisplayName,
        string createdAt)
        => new(
            channel,
            record.Id,
            processDisplayName,
            record.FailedTarget,
            record.FailureStage,
            $"{record.SourceTable}/{record.SourceRecordId?.ToString() ?? "--"}",
            createdAt,
            DiagnosticsTextNormalizer.Normalize(record.FailureReason),
            DiagnosticsTextNormalizer.Normalize(record.CellDataJson));
}

public sealed record DiagnosticsRowsSnapshot(
    IReadOnlyList<ModuleRegistrationRow> ModuleRegistrations,
    IReadOnlyList<PluginLifecycleRow> PluginStates,
    IReadOnlyList<DeviceModuleBindingRow> DeviceBindings,
    IReadOnlyList<StartupDiagnosticIssueRow> Issues,
    IReadOnlyList<MesChannelDiagnosticsRow> MesUploadDiagnostics,
    IReadOnlyList<SyncChannelRow> SyncChannels,
    IReadOnlyList<DeadLetterRow> CloudDeadLetters,
    IReadOnlyList<DeadLetterRow> MesDeadLetters);
