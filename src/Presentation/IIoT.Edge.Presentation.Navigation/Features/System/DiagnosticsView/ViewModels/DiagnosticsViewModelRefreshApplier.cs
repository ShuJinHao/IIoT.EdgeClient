using System.Collections.ObjectModel;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Diagnostics;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsViewModelRefreshApplier
{
    void ApplySummary(DiagnosticsSummarySnapshot summary);

    void ApplyRows(DiagnosticsRowsSnapshot rows);

    void ApplyModuleCounts(StartupDiagnosticsReport report);
}

internal sealed class DiagnosticsViewModelRefreshApplier(
    DiagnosticsSummaryState summaryState,
    DiagnosticsCollectionTargets collections,
    IDiagnosticsViewModelCallback callback)
    : IDiagnosticsViewModelRefreshApplier
{
    public void ApplySummary(DiagnosticsSummarySnapshot summary)
    {
        Set(nameof(DiagnosticsViewModel.DiscoveredModulesSummary), value => summaryState.DiscoveredModulesSummary = value, summary.DiscoveredModulesSummary);
        Set(nameof(DiagnosticsViewModel.EnabledModulesSummary), value => summaryState.EnabledModulesSummary = value, summary.EnabledModulesSummary);
        Set(nameof(DiagnosticsViewModel.ActivatedModulesSummary), value => summaryState.ActivatedModulesSummary = value, summary.ActivatedModulesSummary);
        Set(nameof(DiagnosticsViewModel.ConfigurationProfileSummary), value => summaryState.ConfigurationProfileSummary = value, summary.ConfigurationProfileSummary);
        Set(nameof(DiagnosticsViewModel.ConfigurationEnvironment), value => summaryState.ConfigurationEnvironment = value, summary.ConfigurationEnvironment);
        Set(nameof(DiagnosticsViewModel.ConfigurationMachineProfile), value => summaryState.ConfigurationMachineProfile = value, summary.ConfigurationMachineProfile);
        Set(nameof(DiagnosticsViewModel.ConfigurationMachineProfileState), value => summaryState.ConfigurationMachineProfileState = value, summary.ConfigurationMachineProfileState);
        Set(nameof(DiagnosticsViewModel.ConfigurationRuntimeDataRoot), value => summaryState.ConfigurationRuntimeDataRoot = value, summary.ConfigurationRuntimeDataRoot);
        Set(nameof(DiagnosticsViewModel.LastUpdatedSummary), value => summaryState.LastUpdatedSummary = value, summary.LastUpdatedSummary);
        Set(nameof(DiagnosticsViewModel.DeviceSummary), value => summaryState.DeviceSummary = value, summary.DeviceSummary);
        Set(nameof(DiagnosticsViewModel.CloudGateSummary), value => summaryState.CloudGateSummary = value, summary.CloudGateSummary);
        Set(nameof(DiagnosticsViewModel.CloudRuntimeSummary), value => summaryState.CloudRuntimeSummary = value, summary.CloudRuntimeSummary);
        Set(nameof(DiagnosticsViewModel.CloudResultSummary), value => summaryState.CloudResultSummary = value, summary.CloudResultSummary);
        Set(nameof(DiagnosticsViewModel.CloudPendingSummary), value => summaryState.CloudPendingSummary = value, summary.CloudPendingSummary);
        Set(nameof(DiagnosticsViewModel.CloudCapacitySummary), value => summaryState.CloudCapacitySummary = value, summary.CloudCapacitySummary);
        Set(nameof(DiagnosticsViewModel.CloudPersistenceSummary), value => summaryState.CloudPersistenceSummary = value, summary.CloudPersistenceSummary);
        Set(nameof(DiagnosticsViewModel.CloudLastAttemptSummary), value => summaryState.CloudLastAttemptSummary = value, summary.CloudLastAttemptSummary);
        Set(nameof(DiagnosticsViewModel.CloudLastSuccessSummary), value => summaryState.CloudLastSuccessSummary = value, summary.CloudLastSuccessSummary);
        Set(nameof(DiagnosticsViewModel.CloudLastFailureSummary), value => summaryState.CloudLastFailureSummary = value, summary.CloudLastFailureSummary);
        Set(nameof(DiagnosticsViewModel.MesRuntimeSummary), value => summaryState.MesRuntimeSummary = value, summary.MesRuntimeSummary);
        Set(nameof(DiagnosticsViewModel.MesPendingSummary), value => summaryState.MesPendingSummary = value, summary.MesPendingSummary);
        Set(nameof(DiagnosticsViewModel.MesCapacitySummary), value => summaryState.MesCapacitySummary = value, summary.MesCapacitySummary);
        Set(nameof(DiagnosticsViewModel.MesPersistenceSummary), value => summaryState.MesPersistenceSummary = value, summary.MesPersistenceSummary);
        Set(nameof(DiagnosticsViewModel.MesLastAttemptSummary), value => summaryState.MesLastAttemptSummary = value, summary.MesLastAttemptSummary);
        Set(nameof(DiagnosticsViewModel.MesLastSuccessSummary), value => summaryState.MesLastSuccessSummary = value, summary.MesLastSuccessSummary);
        Set(nameof(DiagnosticsViewModel.MesLastFailureSummary), value => summaryState.MesLastFailureSummary = value, summary.MesLastFailureSummary);
        Set(nameof(DiagnosticsViewModel.ContextPersistenceSummary), value => summaryState.ContextPersistenceSummary = value, summary.ContextPersistenceSummary);
        Set(nameof(DiagnosticsViewModel.ContextCorruptFileCount), value => summaryState.ContextCorruptFileCount = value, summary.ContextCorruptFileCount);
        Set(nameof(DiagnosticsViewModel.ContextLastCorruptDetectedAt), value => summaryState.ContextLastCorruptDetectedAt = value, summary.ContextLastCorruptDetectedAt);
        Set(nameof(DiagnosticsViewModel.HasStartupReport), value => summaryState.HasStartupReport = value, summary.HasStartupReport);
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.ContextPersistenceVisualStatus));
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.ContextSummaryItems));
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.ConfigurationSummaryItems));
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.ModuleReadinessSummary));

        if (!string.IsNullOrWhiteSpace(summary.StartupStatusMessage))
        {
            callback.SetStatus(summary.StartupStatusMessage);
        }
    }

    public void ApplyRows(DiagnosticsRowsSnapshot rows)
    {
        ReplaceItems(collections.ModuleRegistrations, rows.ModuleRegistrations);
        ReplaceItems(collections.PluginStates, rows.PluginStates);
        ReplaceItems(collections.DeviceBindings, rows.DeviceBindings);
        ReplaceItems(collections.ModuleReadinessRows, rows.ModuleReadinessRows);
        ReplaceItems(collections.Issues, rows.Issues);
        ReplaceItems(collections.MesUploadDiagnostics, rows.MesUploadDiagnostics);
        ReplaceItems(collections.SyncChannels, rows.SyncChannels);
        ReplaceItems(collections.CloudDeadLetters, rows.CloudDeadLetters);
        ReplaceItems(collections.MesDeadLetters, rows.MesDeadLetters);

        Set(nameof(DiagnosticsViewModel.CloudDeadLetterCount), value => summaryState.CloudDeadLetterCount = value, collections.CloudDeadLetters.Count);
        Set(nameof(DiagnosticsViewModel.MesDeadLetterCount), value => summaryState.MesDeadLetterCount = value, collections.MesDeadLetters.Count);
        Set(nameof(DiagnosticsViewModel.TotalIssueCount), value => summaryState.TotalIssueCount = value, rows.StartupIssueCount);
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.HasStartupIssues));
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.IsStartupHealthy));
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.ModuleReadinessStatus));
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.ModuleReadinessStatusText));
    }

    public void ApplyModuleCounts(StartupDiagnosticsReport report)
    {
        Set(nameof(DiagnosticsViewModel.DiscoveredModuleCount), value => summaryState.DiscoveredModuleCount = value, report.DiscoveredModules.Count);
        Set(nameof(DiagnosticsViewModel.EnabledModuleCount), value => summaryState.EnabledModuleCount = value, report.EnabledModules.Count);
        Set(nameof(DiagnosticsViewModel.ActivatedModuleCount), value => summaryState.ActivatedModuleCount = value, report.ActivatedModules.Count);
    }

    private void Set<T>(string propertyName, Action<T> assign, T value)
    {
        assign(value);
        callback.NotifyPropertyChanged(propertyName);
    }

    private static void ReplaceItems<TItem>(ObservableCollection<TItem> target, IEnumerable<TItem> items)
    {
        var nextItems = items as IReadOnlyList<TItem> ?? items.ToList();
        if (target.Count == nextItems.Count
            && target.SequenceEqual(nextItems, EqualityComparer<TItem>.Default))
        {
            return;
        }

        target.Clear();
        foreach (var item in nextItems)
        {
            target.Add(item);
        }
    }
}

internal sealed record DiagnosticsCollectionTargets(
    ObservableCollection<ModuleRegistrationRow> ModuleRegistrations,
    ObservableCollection<PluginLifecycleRow> PluginStates,
    ObservableCollection<DeviceModuleBindingRow> DeviceBindings,
    ObservableCollection<ModuleReadinessRow> ModuleReadinessRows,
    ObservableCollection<StartupDiagnosticIssueRow> Issues,
    ObservableCollection<MesChannelDiagnosticsRow> MesUploadDiagnostics,
    ObservableCollection<SyncChannelRow> SyncChannels,
    ObservableCollection<DeadLetterRow> CloudDeadLetters,
    ObservableCollection<DeadLetterRow> MesDeadLetters);

internal sealed class DiagnosticsSummaryState
{
    public int CloudDeadLetterCount { get; set; }
    public int MesDeadLetterCount { get; set; }
    public int TotalIssueCount { get; set; }
    public int DiscoveredModuleCount { get; set; }
    public int EnabledModuleCount { get; set; }
    public int ActivatedModuleCount { get; set; }
    public bool HasStartupReport { get; set; }
    public string DiscoveredModulesSummary { get; set; } = string.Empty;
    public string EnabledModulesSummary { get; set; } = string.Empty;
    public string ActivatedModulesSummary { get; set; } = string.Empty;
    public string ConfigurationProfileSummary { get; set; } = string.Empty;
    public string ConfigurationEnvironment { get; set; } = string.Empty;
    public string ConfigurationMachineProfile { get; set; } = string.Empty;
    public string ConfigurationMachineProfileState { get; set; } = string.Empty;
    public string ConfigurationRuntimeDataRoot { get; set; } = string.Empty;
    public string LastUpdatedSummary { get; set; } = string.Empty;
    public string DeviceSummary { get; set; } = string.Empty;
    public string CloudGateSummary { get; set; } = string.Empty;
    public string CloudRuntimeSummary { get; set; } = string.Empty;
    public string CloudResultSummary { get; set; } = string.Empty;
    public string CloudPendingSummary { get; set; } = string.Empty;
    public string CloudCapacitySummary { get; set; } = string.Empty;
    public string CloudPersistenceSummary { get; set; } = string.Empty;
    public string CloudLastAttemptSummary { get; set; } = string.Empty;
    public string CloudLastSuccessSummary { get; set; } = string.Empty;
    public string CloudLastFailureSummary { get; set; } = string.Empty;
    public string MesRuntimeSummary { get; set; } = string.Empty;
    public string MesPendingSummary { get; set; } = string.Empty;
    public string MesCapacitySummary { get; set; } = string.Empty;
    public string MesPersistenceSummary { get; set; } = string.Empty;
    public string MesLastAttemptSummary { get; set; } = string.Empty;
    public string MesLastSuccessSummary { get; set; } = string.Empty;
    public string MesLastFailureSummary { get; set; } = string.Empty;
    public string ContextPersistenceSummary { get; set; } = string.Empty;
    public string ContextCorruptFileCount { get; set; } = string.Empty;
    public string ContextLastCorruptDetectedAt { get; set; } = string.Empty;
}
