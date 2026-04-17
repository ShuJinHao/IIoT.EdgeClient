using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public sealed class DiagnosticsViewModel : PresentationViewModelBase
{
    private readonly IStartupDiagnosticsStore _diagnosticsStore;
    private readonly IMesUploadDiagnosticsStore _mesUploadDiagnosticsStore;
    private readonly DispatcherTimer _refreshTimer;

    public override string ViewId => CoreViewIds.Diagnostics;

    public override string ViewTitle => "System Diagnostics";

    public ObservableCollection<ModuleRegistrationSnapshot> ModuleRegistrations { get; } = [];

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

    public DiagnosticsViewModel(
        IStartupDiagnosticsStore diagnosticsStore,
        IMesUploadDiagnosticsStore mesUploadDiagnosticsStore)
    {
        _diagnosticsStore = diagnosticsStore;
        _mesUploadDiagnosticsStore = mesUploadDiagnosticsStore;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
    }

    public override Task OnActivatedAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    private void Refresh()
    {
        var report = _diagnosticsStore.Current;

        DiscoveredModulesSummary = report.DiscoveredModules.Count == 0
            ? "No compiled modules were discovered."
            : $"Discovered: {string.Join(", ", report.DiscoveredModules)}";

        EnabledModulesSummary = report.EnabledModules.Count == 0
            ? "No modules are currently enabled."
            : $"Enabled: {string.Join(", ", report.EnabledModules)}";

        LastUpdatedSummary = report.GeneratedAt == DateTime.MinValue
            ? "Startup diagnostics have not been generated yet."
            : $"Last generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}";

        ReplaceItems(ModuleRegistrations, report.ModuleRegistrations);
        ReplaceItems(DeviceBindings, report.DeviceBindings);
        ReplaceItems(Issues, report.Issues);
        ReplaceItems(MesUploadDiagnostics, _mesUploadDiagnosticsStore.GetAll());

        SetStatus(report.Issues.Count == 0
            ? "Startup diagnostics report is healthy."
            : $"Startup diagnostics report contains {report.Issues.Count} issue(s).");
    }
}
