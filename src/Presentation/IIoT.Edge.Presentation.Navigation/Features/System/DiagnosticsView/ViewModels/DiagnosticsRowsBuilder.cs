using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

internal sealed class DiagnosticsRowsBuilder(
    LocalizedSyncDiagnosticsText diagnosticsText,
    DiagnosticsModuleDisplayNameResolver displayNameResolver)
{
    public DiagnosticsRowsSnapshot Build(
        StartupDiagnosticsReport report,
        EdgeSyncDiagnosticsSnapshot syncDiagnostics,
        IReadOnlyDictionary<string, string> moduleNameMap)
    {
        return new DiagnosticsRowsSnapshot(
            BuildModuleRegistrations(report, moduleNameMap),
            BuildPluginStates(report),
            BuildDeviceBindings(report),
            BuildIssues(report),
            BuildMesUploadDiagnostics(syncDiagnostics),
            BuildDeadLetters(DataPipelineRetryChannel.Cloud, syncDiagnostics.Cloud.DeadLetters?.LatestRecords),
            BuildDeadLetters(DataPipelineRetryChannel.Mes, syncDiagnostics.Mes.DeadLetters?.LatestRecords));
    }

    private IReadOnlyList<ModuleRegistrationRow> BuildModuleRegistrations(
        StartupDiagnosticsReport report,
        IReadOnlyDictionary<string, string> moduleNameMap)
        => report.ModuleRegistrations
            .Select(x => new ModuleRegistrationRow(
                x.ModuleId,
                displayNameResolver.ResolveProcessDisplayName(x.ModuleId, x.ProcessType, moduleNameMap),
                x.AssemblyName,
                x.IsEnabled,
                x.HasCellDataRegistration,
                x.HasRuntimeFactory,
                x.HasCloudUploader,
                x.HasMesUploader,
                x.HasHardwareProfile))
            .ToArray();

    private IReadOnlyList<PluginLifecycleRow> BuildPluginStates(StartupDiagnosticsReport report)
        => report.PluginStates
            .Select(x => new PluginLifecycleRow(
                x.ModuleId,
                displayNameResolver.ResolveProcessDisplayName(x.ProcessType, x.DisplayName),
                diagnosticsText.FormatProcessType(x.ProcessType),
                x.Version,
                diagnosticsText.FormatPluginLifecycleState(x.State),
                DiagnosticsTextNormalizer.Normalize(x.Message)))
            .ToArray();

    private static IReadOnlyList<DeviceModuleBindingRow> BuildDeviceBindings(StartupDiagnosticsReport report)
        => report.DeviceBindings
            .Select(x => new DeviceModuleBindingRow(
                x.DeviceName,
                DiagnosticsTextNormalizer.Normalize(x.ModuleId),
                x.ModuleExists,
                x.ModuleEnabled,
                x.HasIoMappings))
            .ToArray();

    private static IReadOnlyList<StartupDiagnosticIssueRow> BuildIssues(StartupDiagnosticsReport report)
        => report.Issues
            .Select(x => new StartupDiagnosticIssueRow(
                x.Code,
                DiagnosticsTextNormalizer.Normalize(x.ModuleId),
                DiagnosticsTextNormalizer.Normalize(x.DeviceName),
                DiagnosticsTextNormalizer.Normalize(x.Message)))
            .ToArray();

    private IReadOnlyList<MesChannelDiagnosticsRow> BuildMesUploadDiagnostics(
        EdgeSyncDiagnosticsSnapshot syncDiagnostics)
        => syncDiagnostics.Mes.Channels
            .Select(x => new MesChannelDiagnosticsRow(
                displayNameResolver.ResolveProcessDisplayName(x.ProcessType, x.ProcessDisplayName),
                diagnosticsText.FormatMesChannelResult(x.LastResult),
                diagnosticsText.FormatTimestamp(x.LastAttemptAt),
                diagnosticsText.FormatTimestamp(x.LastSuccessAt),
                DiagnosticsTextNormalizer.Normalize(x.LastFailureReason)))
            .ToArray();

    private IReadOnlyList<DeadLetterRow> BuildDeadLetters(
        DataPipelineRetryChannel channel,
        IReadOnlyList<DeadLetterRecord>? records)
        => (records ?? [])
            .Select(x => DeadLetterRow.From(
                channel,
                x,
                displayNameResolver.ResolveProcessDisplayName(x.ProcessType, null),
                diagnosticsText.FormatTimestamp(x.CreatedAt)))
            .ToArray();
}
