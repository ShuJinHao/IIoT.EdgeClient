using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Threading;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public sealed class DiagnosticsViewModel : PresentationViewModelBase
{
    private readonly IStartupDiagnosticsStore _diagnosticsStore;
    private readonly IEdgeSyncDiagnosticsQuery _syncDiagnosticsQuery;
    private readonly DispatcherTimer _refreshTimer;
    private int _refreshInProgress;

    public override string ViewId => CoreViewIds.Diagnostics;

    public override string ViewTitle => "System Diagnostics";

    public ObservableCollection<ModuleRegistrationSnapshot> ModuleRegistrations { get; } = [];

    public ObservableCollection<PluginLifecycleSnapshot> PluginStates { get; } = [];

    public ObservableCollection<DeviceModuleBindingSnapshot> DeviceBindings { get; } = [];

    public ObservableCollection<StartupDiagnosticIssue> Issues { get; } = [];

    public ObservableCollection<MesChannelDiagnostics> MesUploadDiagnostics { get; } = [];

    private string _discoveredModulesSummary = "Checking discovered modules...";
    public string DiscoveredModulesSummary
    {
        get => _discoveredModulesSummary;
        private set
        {
            _discoveredModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _enabledModulesSummary = "Checking enabled modules...";
    public string EnabledModulesSummary
    {
        get => _enabledModulesSummary;
        private set
        {
            _enabledModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _activatedModulesSummary = "Checking activated modules...";
    public string ActivatedModulesSummary
    {
        get => _activatedModulesSummary;
        private set
        {
            _activatedModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _configurationProfileSummary = "Configuration profile: --";
    public string ConfigurationProfileSummary
    {
        get => _configurationProfileSummary;
        private set
        {
            _configurationProfileSummary = value;
            OnPropertyChanged();
        }
    }

    private string _lastUpdatedSummary = "No startup diagnostics have been captured yet.";
    public string LastUpdatedSummary
    {
        get => _lastUpdatedSummary;
        private set
        {
            _lastUpdatedSummary = value;
            OnPropertyChanged();
        }
    }

    private string _deviceSummary = "Device: Unknown";
    public string DeviceSummary
    {
        get => _deviceSummary;
        private set
        {
            _deviceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudGateSummary = "Cloud gate: --";
    public string CloudGateSummary
    {
        get => _cloudGateSummary;
        private set
        {
            _cloudGateSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudRuntimeSummary = "Cloud runtime: Idle";
    public string CloudRuntimeSummary
    {
        get => _cloudRuntimeSummary;
        private set
        {
            _cloudRuntimeSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudResultSummary = "Cloud last result: --";
    public string CloudResultSummary
    {
        get => _cloudResultSummary;
        private set
        {
            _cloudResultSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudPendingSummary = "Cloud pending: retry=0, logs=0, capacity=0";
    public string CloudPendingSummary
    {
        get => _cloudPendingSummary;
        private set
        {
            _cloudPendingSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudCapacitySummary = "Capacity blocked: no";
    public string CloudCapacitySummary
    {
        get => _cloudCapacitySummary;
        private set
        {
            _cloudCapacitySummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudPersistenceSummary = "Storage fault: no";
    public string CloudPersistenceSummary
    {
        get => _cloudPersistenceSummary;
        private set
        {
            _cloudPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastAttemptSummary = "Cloud last attempt: --";
    public string CloudLastAttemptSummary
    {
        get => _cloudLastAttemptSummary;
        private set
        {
            _cloudLastAttemptSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastSuccessSummary = "Cloud last success: --";
    public string CloudLastSuccessSummary
    {
        get => _cloudLastSuccessSummary;
        private set
        {
            _cloudLastSuccessSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastFailureSummary = "Cloud last failure: --";
    public string CloudLastFailureSummary
    {
        get => _cloudLastFailureSummary;
        private set
        {
            _cloudLastFailureSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesRuntimeSummary = "MES runtime: Idle";
    public string MesRuntimeSummary
    {
        get => _mesRuntimeSummary;
        private set
        {
            _mesRuntimeSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesPendingSummary = "MES pending: retry=0";
    public string MesPendingSummary
    {
        get => _mesPendingSummary;
        private set
        {
            _mesPendingSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesCapacitySummary = "Capacity blocked: no";
    public string MesCapacitySummary
    {
        get => _mesCapacitySummary;
        private set
        {
            _mesCapacitySummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesPersistenceSummary = "Storage fault: no";
    public string MesPersistenceSummary
    {
        get => _mesPersistenceSummary;
        private set
        {
            _mesPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastAttemptSummary = "MES last attempt: --";
    public string MesLastAttemptSummary
    {
        get => _mesLastAttemptSummary;
        private set
        {
            _mesLastAttemptSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastSuccessSummary = "MES last success: --";
    public string MesLastSuccessSummary
    {
        get => _mesLastSuccessSummary;
        private set
        {
            _mesLastSuccessSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastFailureSummary = "MES last failure: --";
    public string MesLastFailureSummary
    {
        get => _mesLastFailureSummary;
        private set
        {
            _mesLastFailureSummary = value;
            OnPropertyChanged();
        }
    }

    private string _contextPersistenceSummary = "Corrupt files: 0";
    public string ContextPersistenceSummary
    {
        get => _contextPersistenceSummary;
        private set
        {
            _contextPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    public DiagnosticsViewModel(
        IStartupDiagnosticsStore diagnosticsStore,
        IEdgeSyncDiagnosticsQuery syncDiagnosticsQuery)
    {
        _diagnosticsStore = diagnosticsStore;
        _syncDiagnosticsQuery = syncDiagnosticsQuery;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
    }

    public override Task OnActivatedAsync() => RefreshAsync();

    internal Task RefreshAsync(CancellationToken ct = default)
        => RefreshIfIdleAsync(ct);

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await SafeRefreshAsync();
    }

    private async Task SafeRefreshAsync(CancellationToken ct = default)
    {
        try
        {
            await RefreshIfIdleAsync(ct);
        }
        catch
        {
            // Diagnostics refresh should not crash the periodic UI loop.
        }
    }

    private async Task RefreshIfIdleAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            await RefreshCoreAsync(ct);
        }
        finally
        {
            Volatile.Write(ref _refreshInProgress, 0);
        }
    }

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        var report = _diagnosticsStore.Current;
        var syncDiagnostics = await _syncDiagnosticsQuery.GetCurrentAsync(ct);

        DiscoveredModulesSummary = report.DiscoveredModules.Count == 0
            ? "No plugins were discovered."
            : $"Discovered: {string.Join(", ", report.DiscoveredModules)}";

        EnabledModulesSummary = report.EnabledModules.Count == 0
            ? "No modules are configured as enabled."
            : $"Configured enabled: {string.Join(", ", report.EnabledModules)}";

        ActivatedModulesSummary = report.ActivatedModules.Count == 0
            ? "No plugins are currently activated."
            : $"Activated: {string.Join(", ", report.ActivatedModules)}";

        ConfigurationProfileSummary = BuildConfigurationProfileSummary(report.ConfigurationProfile);

        LastUpdatedSummary = report.GeneratedAt == DateTime.MinValue
            ? "Startup diagnostics have not been generated yet."
            : $"Last generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}";

        DeviceSummary = $"Device: {syncDiagnostics.DeviceName}";

        var cloudGate = syncDiagnostics.Cloud.GateState switch
        {
            EdgeUploadGateState.Ready => "Ready",
            _ when syncDiagnostics.Cloud.IsPausedWaitingForRecovery => "Waiting for Recovery",
            _ => $"Blocked ({EdgeSyncDiagnosticsFormatter.FormatBlockReason(syncDiagnostics.Cloud.BlockReason)})"
        };
        CloudGateSummary = $"Cloud gate: {cloudGate}";
        CloudRuntimeSummary = $"Cloud runtime: {syncDiagnostics.Cloud.RuntimeState}";
        CloudResultSummary =
            $"Cloud last result: {EdgeSyncDiagnosticsFormatter.FormatCloudOutcome(syncDiagnostics.Cloud.LastOutcome, syncDiagnostics.Cloud.LastReasonCode, syncDiagnostics.Cloud.LastProcessType)}";
        CloudPendingSummary =
            $"Cloud pending: retry={syncDiagnostics.Cloud.PendingRetryCount}, logs={syncDiagnostics.Cloud.PendingDeviceLogCount}, capacity={syncDiagnostics.Cloud.PendingCapacityCount}";
        CloudCapacitySummary = EdgeSyncDiagnosticsFormatter.FormatCapacityBlockedSummary(
            syncDiagnostics.Cloud.IsCapacityBlocked,
            syncDiagnostics.Cloud.BlockedChannel,
            syncDiagnostics.Cloud.BlockedReason,
            syncDiagnostics.Cloud.LastCapacityBlockAt);
        CloudPersistenceSummary = EdgeSyncDiagnosticsFormatter.FormatPersistenceFaultSummary(
            syncDiagnostics.Cloud.IsPersistenceFaulted,
            syncDiagnostics.Cloud.LastPersistenceFaultAt,
            syncDiagnostics.Cloud.PersistenceFaultMessage);
        CloudLastAttemptSummary = $"Cloud last attempt: {EdgeSyncDiagnosticsFormatter.FormatTimestamp(syncDiagnostics.Cloud.LastAttemptAt)}";
        CloudLastSuccessSummary = $"Cloud last success: {EdgeSyncDiagnosticsFormatter.FormatTimestamp(syncDiagnostics.Cloud.LastSuccessAt)}";
        CloudLastFailureSummary = $"Cloud last failure: {EdgeSyncDiagnosticsFormatter.FormatTimestamp(syncDiagnostics.Cloud.LastFailureAt)}";

        MesRuntimeSummary = $"MES runtime: {syncDiagnostics.Mes.RuntimeState}";
        MesPendingSummary = $"MES pending: retry={syncDiagnostics.Mes.PendingRetryCount}";
        MesCapacitySummary = EdgeSyncDiagnosticsFormatter.FormatCapacityBlockedSummary(
            syncDiagnostics.Mes.IsCapacityBlocked,
            syncDiagnostics.Mes.BlockedChannel,
            syncDiagnostics.Mes.BlockedReason,
            syncDiagnostics.Mes.LastCapacityBlockAt);
        MesPersistenceSummary = EdgeSyncDiagnosticsFormatter.FormatPersistenceFaultSummary(
            syncDiagnostics.Mes.IsPersistenceFaulted,
            syncDiagnostics.Mes.LastPersistenceFaultAt,
            syncDiagnostics.Mes.PersistenceFaultMessage);
        MesLastAttemptSummary = $"MES last attempt: {EdgeSyncDiagnosticsFormatter.FormatTimestamp(syncDiagnostics.Mes.LastAttemptAt)}";
        MesLastSuccessSummary = $"MES last success: {EdgeSyncDiagnosticsFormatter.FormatTimestamp(syncDiagnostics.Mes.LastSuccessAt)}";
        MesLastFailureSummary =
            $"MES last failure: {EdgeSyncDiagnosticsFormatter.FormatTimestamp(syncDiagnostics.Mes.LastFailureAt)} ({syncDiagnostics.Mes.LastFailureReason ?? "--"})";
        ContextPersistenceSummary = EdgeSyncDiagnosticsFormatter.FormatContextPersistenceSummary(syncDiagnostics.ContextPersistence);

        ReplaceItems(ModuleRegistrations, report.ModuleRegistrations);
        ReplaceItems(PluginStates, report.PluginStates);
        ReplaceItems(DeviceBindings, report.DeviceBindings);
        ReplaceItems(Issues, report.Issues);
        ReplaceItems(MesUploadDiagnostics, syncDiagnostics.Mes.Channels);

        SetStatus(report.Issues.Count == 0
            ? "Startup diagnostics report is healthy."
            : $"Startup diagnostics report contains {report.Issues.Count} issue(s).");
    }

    private static string BuildConfigurationProfileSummary(ConfigurationProfileSnapshot profile)
    {
        if (string.IsNullOrWhiteSpace(profile.MachineProfile))
        {
            return $"Environment: {profile.EnvironmentName}; Machine profile: <none>";
        }

        var state = profile.IsMachineProfileLoaded
            ? $"loaded from {profile.MachineProfileFileName}"
            : $"missing file {profile.MachineProfileFileName}";
        return $"Environment: {profile.EnvironmentName}; Machine profile: {profile.MachineProfile} ({state})";
    }
}
