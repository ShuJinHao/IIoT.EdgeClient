using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public sealed class DiagnosticsViewModel : NavigationViewModelBase, IDiagnosticsViewModelCallback
{
    private readonly IStartupDiagnosticsStore _diagnosticsStore;
    private readonly IEdgeSyncDiagnosticsQuery _syncDiagnosticsQuery;
    private readonly IDiagnosticsModuleDisplayNameResolver _displayNameResolver;
    private readonly IDiagnosticsSummaryBuilder _summaryBuilder;
    private readonly IDiagnosticsRowsBuilder _rowsBuilder;
    private readonly IDiagnosticsInitialSummaryFactory _initialSummaryFactory;
    private readonly IDiagnosticsRefreshCoordinator _refreshCoordinator;
    private readonly AsyncCommand<DeadLetterRow> _requeueDeadLetterCommand;
    private readonly AsyncCommand<DeadLetterRow> _deleteDeadLetterCommand;
    private readonly Avalonia.Threading.DispatcherTimer _refreshTimer;
    private readonly DiagnosticsSummaryState _summaryState = new();
    private readonly IDiagnosticsTabController _tabController;
    private readonly IDiagnosticsViewModelRefreshApplier _refreshApplier;
    private readonly IDiagnosticsDeadLetterCommandWorkflow _deadLetterWorkflow;
    private readonly IDiagnosticsPermissionObserver _permissionObserver;
    private bool _isModuleReadinessExpanded;
    private bool _isObserving;

    public DiagnosticsViewModel(
        IStartupDiagnosticsStore diagnosticsStore,
        IEdgeSyncDiagnosticsQuery syncDiagnosticsQuery,
        IAppLanguageService languageService,
        IDiagnosticsModuleDisplayNameResolver displayNameResolver,
        IDiagnosticsSummaryBuilder summaryBuilder,
        IDiagnosticsRowsBuilder rowsBuilder,
        IDiagnosticsInitialSummaryFactory initialSummaryFactory,
        IDiagnosticsRefreshCoordinator refreshCoordinator,
        IDiagnosticsViewModelCollaboratorFactory collaboratorFactory)
        : base(languageService, CoreViewIds.Diagnostics, "Navigation_Menu_CoreDiagnostics", "系统诊断")
    {
        _diagnosticsStore = diagnosticsStore;
        _syncDiagnosticsQuery = syncDiagnosticsQuery;
        _displayNameResolver = displayNameResolver;
        _summaryBuilder = summaryBuilder;
        _rowsBuilder = rowsBuilder;
        _initialSummaryFactory = initialSummaryFactory;
        _refreshCoordinator = refreshCoordinator;
        var collaborators = collaboratorFactory.Create(new DiagnosticsViewModelCollaboratorContext(
            this,
            Tabs,
            _summaryState,
            new DiagnosticsCollectionTargets(
                ModuleRegistrations,
                PluginStates,
                DeviceBindings,
                ModuleReadinessRows,
                Issues,
                MesUploadDiagnostics,
                SyncChannels,
                CloudDeadLetters,
                MesDeadLetters)));
        _tabController = collaborators.TabController;
        _refreshApplier = collaborators.RefreshApplier;
        _deadLetterWorkflow = collaborators.DeadLetterWorkflow;
        _permissionObserver = collaborators.PermissionObserver;

        _requeueDeadLetterCommand = new AsyncCommand<DeadLetterRow>(RequeueDeadLetterAsync, CanOperateDeadLetter);
        _deleteDeadLetterCommand = new AsyncCommand<DeadLetterRow>(DeleteDeadLetterAsync, CanOperateDeadLetter);
        RequeueDeadLetterCommand = _requeueDeadLetterCommand;
        DeleteDeadLetterCommand = _deleteDeadLetterCommand;
        ToggleModuleReadinessCommand = new BaseCommand(_ => ToggleModuleReadiness());
        _refreshTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        SelectTabCommand = new BaseCommand(parameter =>
        {
            if (parameter is DiagnosticsTabItemViewModel tab)
            {
                SelectTab(tab);
            }
        });
        _tabController.Initialize();
        ApplyInitialSummaries();
    }

    public ObservableCollection<DiagnosticsTabItemViewModel> Tabs { get; } = [];
    public ICommand SelectTabCommand { get; private set; } = null!;

    public DiagnosticsTabItemViewModel SelectedTab
    {
        get => _tabController.SelectedTab;
        set => SelectTab(value);
    }

    public bool IsSyncOpsTabSelected => _tabController.IsSyncOpsTabSelected;
    public bool IsStartupTabSelected => _tabController.IsStartupTabSelected;

    public ObservableCollection<ModuleRegistrationRow> ModuleRegistrations { get; } = [];
    public ObservableCollection<PluginLifecycleRow> PluginStates { get; } = [];
    public ObservableCollection<DeviceModuleBindingRow> DeviceBindings { get; } = [];
    public ObservableCollection<ModuleReadinessRow> ModuleReadinessRows { get; } = [];
    public ObservableCollection<StartupDiagnosticIssueRow> Issues { get; } = [];
    public ObservableCollection<MesChannelDiagnosticsRow> MesUploadDiagnostics { get; } = [];
    public ObservableCollection<SyncChannelRow> SyncChannels { get; } = [];
    public ObservableCollection<DeadLetterRow> CloudDeadLetters { get; } = [];
    public ObservableCollection<DeadLetterRow> MesDeadLetters { get; } = [];

    public ICommand RequeueDeadLetterCommand { get; }
    public ICommand DeleteDeadLetterCommand { get; }
    public ICommand ToggleModuleReadinessCommand { get; }

    public bool CanOperateDeadLetters => _permissionObserver.CanOperateDeadLetters;
    public bool HasStartupReport => _summaryState.HasStartupReport;
    public bool HasStartupIssues => HasStartupReport && Issues.Count > 0;
    public bool IsStartupHealthy => HasStartupReport && Issues.Count == 0;
    public bool IsModuleReadinessExpanded => _isModuleReadinessExpanded;
    public bool IsModuleReadinessCollapsed => !_isModuleReadinessExpanded;
    public int CloudDeadLetterCount => _summaryState.CloudDeadLetterCount;
    public int MesDeadLetterCount => _summaryState.MesDeadLetterCount;
    public int TotalIssueCount => _summaryState.TotalIssueCount;
    public int DiscoveredModuleCount => _summaryState.DiscoveredModuleCount;
    public int EnabledModuleCount => _summaryState.EnabledModuleCount;
    public int ActivatedModuleCount => _summaryState.ActivatedModuleCount;
    public string DiscoveredModulesSummary => _summaryState.DiscoveredModulesSummary;
    public string EnabledModulesSummary => _summaryState.EnabledModulesSummary;
    public string ActivatedModulesSummary => _summaryState.ActivatedModulesSummary;
    public string ConfigurationProfileSummary => _summaryState.ConfigurationProfileSummary;
    public string ConfigurationEnvironment => _summaryState.ConfigurationEnvironment;
    public string ConfigurationMachineProfile => _summaryState.ConfigurationMachineProfile;
    public string ConfigurationMachineProfileState => _summaryState.ConfigurationMachineProfileState;
    public string ConfigurationRuntimeDataRoot => _summaryState.ConfigurationRuntimeDataRoot;
    public string LastUpdatedSummary => _summaryState.LastUpdatedSummary;
    public string DeviceSummary => _summaryState.DeviceSummary;
    public string CloudGateSummary => _summaryState.CloudGateSummary;
    public string CloudRuntimeSummary => _summaryState.CloudRuntimeSummary;
    public string CloudResultSummary => _summaryState.CloudResultSummary;
    public string CloudPendingSummary => _summaryState.CloudPendingSummary;
    public string CloudCapacitySummary => _summaryState.CloudCapacitySummary;
    public string CloudPersistenceSummary => _summaryState.CloudPersistenceSummary;
    public string CloudLastAttemptSummary => _summaryState.CloudLastAttemptSummary;
    public string CloudLastSuccessSummary => _summaryState.CloudLastSuccessSummary;
    public string CloudLastFailureSummary => _summaryState.CloudLastFailureSummary;
    public string MesRuntimeSummary => _summaryState.MesRuntimeSummary;
    public string MesPendingSummary => _summaryState.MesPendingSummary;
    public string MesCapacitySummary => _summaryState.MesCapacitySummary;
    public string MesPersistenceSummary => _summaryState.MesPersistenceSummary;
    public string MesLastAttemptSummary => _summaryState.MesLastAttemptSummary;
    public string MesLastSuccessSummary => _summaryState.MesLastSuccessSummary;
    public string MesLastFailureSummary => _summaryState.MesLastFailureSummary;
    public string ContextPersistenceSummary => _summaryState.ContextPersistenceSummary;
    public string ContextCorruptFileCount => _summaryState.ContextCorruptFileCount;
    public string ContextLastCorruptDetectedAt => _summaryState.ContextLastCorruptDetectedAt;
    public IReadOnlyList<EdgeSummaryItem> ContextSummaryItems =>
    [
        new()
        {
            Label = GetText("Navigation_Diagnostics_ContextCorruptCount", "损坏文件数"),
            Value = ContextCorruptFileCount
        },
        new()
        {
            Label = GetText("Navigation_Diagnostics_ContextLastCorrupt", "最近损坏"),
            Value = ContextLastCorruptDetectedAt
        }
    ];

    public IReadOnlyList<EdgeSummaryItem> ConfigurationSummaryItems =>
    [
        new()
        {
            Label = GetText("Navigation_Diagnostics_ConfigEnvironment", "环境"),
            Value = ConfigurationEnvironment
        },
        new()
        {
            Label = GetText("Navigation_Diagnostics_ConfigMachineProfile", "机型 Profile"),
            Value = ConfigurationMachineProfile
        },
        new()
        {
            Label = GetText("Navigation_Diagnostics_ConfigMachineProfileState", "机型状态"),
            Value = ConfigurationMachineProfileState
        },
        new()
        {
            Label = GetText("Navigation_Diagnostics_ConfigRuntimeDataRoot", "运行数据根目录"),
            Value = ConfigurationRuntimeDataRoot
        }
    ];

    public EdgeVisualStatus ContextPersistenceVisualStatus => _summaryState.ContextCorruptFileCount == "0"
        ? EdgeVisualStatus.Running
        : EdgeVisualStatus.Warning;
    public EdgeVisualStatus ModuleReadinessStatus => HasStartupIssues
        ? EdgeVisualStatus.Warning
        : EdgeVisualStatus.Running;
    public string ModuleReadinessStatusText => HasStartupIssues
        ? FormatText("Navigation_Diagnostics_IssueCountFormat", "{0} 个问题", Issues.Count)
        : GetText("Navigation_Diagnostics_ModuleReadinessHealthy", "就绪");
    public string ModuleReadinessSummary => string.Join(Environment.NewLine, [
        DiscoveredModulesSummary,
        EnabledModulesSummary,
        ActivatedModulesSummary
    ]);
    public string ModuleReadinessToggleText => IsModuleReadinessExpanded
        ? GetText("Navigation_Diagnostics_CollapseModuleReadiness", "收起明细")
        : GetText("Navigation_Diagnostics_ExpandModuleReadiness", "展开明细");

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        _tabController.RefreshLanguage();
        OnPropertyChanged(nameof(ModuleReadinessStatusText));
        OnPropertyChanged(nameof(ModuleReadinessToggleText));
        OnPropertyChanged(nameof(ContextSummaryItems));
        OnPropertyChanged(nameof(ConfigurationSummaryItems));
        _ = SafeRefreshAsync();
    }

    public override async Task OnActivatedAsync()
    {
        StartObserving();
        await RefreshAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        StopObserving();
        return Task.CompletedTask;
    }

    internal Task RefreshAsync(CancellationToken ct = default)
        => _refreshCoordinator.RunIfIdleAsync(RefreshCoreAsync, ct);

    private void SelectTab(DiagnosticsTabItemViewModel? tab)
        => _tabController.Select(tab);

    private void ToggleModuleReadiness()
    {
        _isModuleReadinessExpanded = !_isModuleReadinessExpanded;
        OnPropertyChanged(nameof(IsModuleReadinessExpanded));
        OnPropertyChanged(nameof(IsModuleReadinessCollapsed));
        OnPropertyChanged(nameof(ModuleReadinessToggleText));
    }

    private void StartObserving()
    {
        if (_isObserving)
        {
            return;
        }

        _refreshTimer.Tick += OnRefreshTimerTick;
        _permissionObserver.Start();
        _refreshTimer.Start();
        _isObserving = true;
    }

    private void StopObserving()
    {
        if (!_isObserving)
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _permissionObserver.Stop();
        _isObserving = false;
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await SafeRefreshAsync();
    }

    private async Task SafeRefreshAsync(CancellationToken ct = default)
    {
        try
        {
            await RefreshAsync(ct);
        }
        catch
        {
            // 诊断页刷新失败不应导致界面轮询崩溃。
        }
    }

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        var report = _diagnosticsStore.Current;
        var syncDiagnostics = await _syncDiagnosticsQuery.GetCurrentAsync(ct);
        var moduleNameMap = _displayNameResolver.BuildModuleNameMap(report);

        _refreshApplier.ApplySummary(_summaryBuilder.Build(report, syncDiagnostics, moduleNameMap));
        _refreshApplier.ApplyRows(_rowsBuilder.Build(report, syncDiagnostics, moduleNameMap));
        _refreshApplier.ApplyModuleCounts(report);
    }

    private void ApplyInitialSummaries()
        => _refreshApplier.ApplySummary(_initialSummaryFactory.Create());

    private bool CanOperateDeadLetter(DeadLetterRow? row)
        => _deadLetterWorkflow.CanOperate(row);

    private Task RequeueDeadLetterAsync(DeadLetterRow row)
        => _deadLetterWorkflow.RequeueAsync(row);

    private Task DeleteDeadLetterAsync(DeadLetterRow row)
        => _deadLetterWorkflow.DeleteAsync(row);

    private void RefreshPermissionState()
    {
        OnPropertyChanged(nameof(CanOperateDeadLetters));
        _requeueDeadLetterCommand.RaiseCanExecuteChanged();
        _deleteDeadLetterCommand.RaiseCanExecuteChanged();
    }

    bool IDiagnosticsViewModelCallback.CanOperateDeadLetters
        => CanOperateDeadLetters;

    Task IDiagnosticsViewModelCallback.RefreshAsync(CancellationToken cancellationToken)
        => RefreshAsync(cancellationToken);

    void IDiagnosticsViewModelCallback.RefreshPermissionState()
        => RefreshPermissionState();

    void IDiagnosticsViewModelCallback.NotifyPropertyChanged(string propertyName)
        => OnPropertyChanged(propertyName);

    void IDiagnosticsViewModelCallback.SetStatus(string message)
        => SetStatus(message);

    void IDiagnosticsViewModelCallback.SetError(string message)
        => SetError(message);

    string IDiagnosticsViewModelCallback.GetText(string resourceKey, string fallback)
        => GetText(resourceKey, fallback);

    string IDiagnosticsViewModelCallback.FormatText(string resourceKey, string fallback, params object[] arguments)
        => FormatText(resourceKey, fallback, arguments);
}
