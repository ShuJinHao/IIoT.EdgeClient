using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.UI.Shared.Avalonia.Controls;

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

public sealed record ModuleReadinessRow(
    string ModuleId,
    string DisplayName,
    string ProcessType,
    string Version,
    string LifecycleState,
    string DeviceNames,
    bool ModuleRegistered,
    bool PluginActivated,
    bool ModuleEnabled,
    bool HasRuntimeFactory,
    bool HasCloudUploader,
    bool HasMesUploader,
    bool HasIoMappings,
    string Message);

public sealed record StartupDiagnosticIssueRow(
    string Message,
    string LevelText,
    EdgeVisualStatus Status,
    int DuplicateCount,
    string DuplicateBadgeText)
{
    public bool HasDuplicateCount => DuplicateCount > 1;

    public string DisplayMessage => HasDuplicateCount
        ? $"{Message} {DuplicateBadgeText}"
        : Message;
}

public sealed record MesChannelDiagnosticsRow(
    string ProcessType,
    string DeviceName,
    string Scenario,
    string LastResult,
    string LastAttemptAt,
    string LastSuccessAt,
    string LastFailureReason);

public sealed record SyncChannelRow(
    string Channel,
    string Status,
    EdgeVisualStatus VisualStatus,
    string Pending,
    int DeadLetterCount,
    string LastError,
    string Note)
{
    public string DetailText => BuildDetailText(LastError, Note);

    private static string BuildDetailText(string lastError, string note)
    {
        var parts = new[] { lastError, note }
            .Where(static x => !string.IsNullOrWhiteSpace(x) && x != "--")
            .ToArray();
        return parts.Length == 0 ? string.Empty : string.Join(" | ", parts);
    }
}

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
    public string PlcCode { get; init; } = string.Empty;

    public string DeviceName { get; init; } = string.Empty;

    public string TaskKey { get; init; } = string.Empty;

    public string IdentityDisplay
        => string.IsNullOrWhiteSpace(PlcCode)
            ? DiagnosticsTextNormalizer.Normalize(DeviceName)
            : string.IsNullOrWhiteSpace(DeviceName)
              || string.Equals(PlcCode, DeviceName, StringComparison.OrdinalIgnoreCase)
                ? PlcCode
                : $"{PlcCode} · {DeviceName}";

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
            DiagnosticsTextNormalizer.Normalize(record.CellDataJson))
        {
            PlcCode = record.PlcCode,
            DeviceName = record.DeviceName,
            TaskKey = record.TaskKey
        };
}

public sealed record DiagnosticsRowsSnapshot(
    IReadOnlyList<ModuleRegistrationRow> ModuleRegistrations,
    IReadOnlyList<PluginLifecycleRow> PluginStates,
    IReadOnlyList<DeviceModuleBindingRow> DeviceBindings,
    IReadOnlyList<ModuleReadinessRow> ModuleReadinessRows,
    IReadOnlyList<StartupDiagnosticIssueRow> Issues,
    int StartupIssueCount,
    IReadOnlyList<MesChannelDiagnosticsRow> MesUploadDiagnostics,
    IReadOnlyList<SyncChannelRow> SyncChannels,
    IReadOnlyList<DeadLetterRow> CloudDeadLetters,
    IReadOnlyList<DeadLetterRow> MesDeadLetters);
