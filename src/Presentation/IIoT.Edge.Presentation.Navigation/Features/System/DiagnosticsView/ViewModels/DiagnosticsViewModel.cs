using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public sealed class DiagnosticsViewModel : NavigationViewModelBase
{
    private readonly IStartupDiagnosticsStore _diagnosticsStore;
    private readonly IEdgeSyncDiagnosticsQuery _syncDiagnosticsQuery;
    private readonly IDiagnosticsModuleDisplayNameResolver _displayNameResolver;
    private readonly IDiagnosticsSummaryBuilder _summaryBuilder;
    private readonly IDiagnosticsRowsBuilder _rowsBuilder;
    private readonly IDiagnosticsInitialSummaryFactory _initialSummaryFactory;
    private readonly IDiagnosticsRefreshCoordinator _refreshCoordinator;
    private readonly IDiagnosticsDeadLetterOperator _deadLetterOperator;
    private readonly IDiagnosticsDeadLetterConfirmationService _deadLetterConfirmationService;
    private readonly IClientPermissionService _permissionService;
    private readonly AsyncCommand<DeadLetterRow> _requeueDeadLetterCommand;
    private readonly AsyncCommand<DeadLetterRow> _deleteDeadLetterCommand;
    private readonly Avalonia.Threading.DispatcherTimer _refreshTimer;
    private bool _isObserving;
    private DiagnosticsTabItemViewModel _selectedTab = null!;

    public ObservableCollection<DiagnosticsTabItemViewModel> Tabs { get; } = [];
    public ICommand SelectTabCommand { get; private set; } = null!;

    public DiagnosticsTabItemViewModel SelectedTab
    {
        get => _selectedTab;
        set => SelectTab(value);
    }

    public bool IsOverviewTabSelected => _selectedTab?.Key == "Diag.Overview";
    public bool IsSyncOpsTabSelected => _selectedTab?.Key == "Diag.SyncOps";
    public bool IsStartupTabSelected => _selectedTab?.Key == "Diag.Startup";

    public ObservableCollection<ModuleRegistrationRow> ModuleRegistrations { get; } = [];
    public ObservableCollection<PluginLifecycleRow> PluginStates { get; } = [];
    public ObservableCollection<DeviceModuleBindingRow> DeviceBindings { get; } = [];
    public ObservableCollection<StartupDiagnosticIssueRow> Issues { get; } = [];
    public ObservableCollection<MesChannelDiagnosticsRow> MesUploadDiagnostics { get; } = [];
    public ObservableCollection<SyncChannelRow> SyncChannels { get; } = [];
    public ObservableCollection<DeadLetterRow> CloudDeadLetters { get; } = [];
    public ObservableCollection<DeadLetterRow> MesDeadLetters { get; } = [];

    public ICommand RequeueDeadLetterCommand { get; }
    public ICommand DeleteDeadLetterCommand { get; }

    public bool CanOperateDeadLetters => _permissionService.IsLocalAdmin;

    private int _cloudDeadLetterCount;
    public int CloudDeadLetterCount
    {
        get => _cloudDeadLetterCount;
        private set
        {
            _cloudDeadLetterCount = value;
            OnPropertyChanged();
        }
    }

    private int _mesDeadLetterCount;
    public int MesDeadLetterCount
    {
        get => _mesDeadLetterCount;
        private set
        {
            _mesDeadLetterCount = value;
            OnPropertyChanged();
        }
    }

    private int _totalIssueCount;
    public int TotalIssueCount
    {
        get => _totalIssueCount;
        private set
        {
            _totalIssueCount = value;
            OnPropertyChanged();
        }
    }

    private int _discoveredModuleCount;
    public int DiscoveredModuleCount
    {
        get => _discoveredModuleCount;
        private set
        {
            _discoveredModuleCount = value;
            OnPropertyChanged();
        }
    }

    private int _enabledModuleCount;
    public int EnabledModuleCount
    {
        get => _enabledModuleCount;
        private set
        {
            _enabledModuleCount = value;
            OnPropertyChanged();
        }
    }

    private int _activatedModuleCount;
    public int ActivatedModuleCount
    {
        get => _activatedModuleCount;
        private set
        {
            _activatedModuleCount = value;
            OnPropertyChanged();
        }
    }

    private string _discoveredModulesSummary = string.Empty;
    public string DiscoveredModulesSummary
    {
        get => _discoveredModulesSummary;
        private set
        {
            _discoveredModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _enabledModulesSummary = string.Empty;
    public string EnabledModulesSummary
    {
        get => _enabledModulesSummary;
        private set
        {
            _enabledModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _activatedModulesSummary = string.Empty;
    public string ActivatedModulesSummary
    {
        get => _activatedModulesSummary;
        private set
        {
            _activatedModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _configurationProfileSummary = string.Empty;
    public string ConfigurationProfileSummary
    {
        get => _configurationProfileSummary;
        private set
        {
            _configurationProfileSummary = value;
            OnPropertyChanged();
        }
    }

    private string _lastUpdatedSummary = string.Empty;
    public string LastUpdatedSummary
    {
        get => _lastUpdatedSummary;
        private set
        {
            _lastUpdatedSummary = value;
            OnPropertyChanged();
        }
    }

    private string _deviceSummary = string.Empty;
    public string DeviceSummary
    {
        get => _deviceSummary;
        private set
        {
            _deviceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudGateSummary = string.Empty;
    public string CloudGateSummary
    {
        get => _cloudGateSummary;
        private set
        {
            _cloudGateSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudRuntimeSummary = string.Empty;
    public string CloudRuntimeSummary
    {
        get => _cloudRuntimeSummary;
        private set
        {
            _cloudRuntimeSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudResultSummary = string.Empty;
    public string CloudResultSummary
    {
        get => _cloudResultSummary;
        private set
        {
            _cloudResultSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudPendingSummary = string.Empty;
    public string CloudPendingSummary
    {
        get => _cloudPendingSummary;
        private set
        {
            _cloudPendingSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudCapacitySummary = string.Empty;
    public string CloudCapacitySummary
    {
        get => _cloudCapacitySummary;
        private set
        {
            _cloudCapacitySummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudPersistenceSummary = string.Empty;
    public string CloudPersistenceSummary
    {
        get => _cloudPersistenceSummary;
        private set
        {
            _cloudPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastAttemptSummary = string.Empty;
    public string CloudLastAttemptSummary
    {
        get => _cloudLastAttemptSummary;
        private set
        {
            _cloudLastAttemptSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastSuccessSummary = string.Empty;
    public string CloudLastSuccessSummary
    {
        get => _cloudLastSuccessSummary;
        private set
        {
            _cloudLastSuccessSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastFailureSummary = string.Empty;
    public string CloudLastFailureSummary
    {
        get => _cloudLastFailureSummary;
        private set
        {
            _cloudLastFailureSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesRuntimeSummary = string.Empty;
    public string MesRuntimeSummary
    {
        get => _mesRuntimeSummary;
        private set
        {
            _mesRuntimeSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesPendingSummary = string.Empty;
    public string MesPendingSummary
    {
        get => _mesPendingSummary;
        private set
        {
            _mesPendingSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesCapacitySummary = string.Empty;
    public string MesCapacitySummary
    {
        get => _mesCapacitySummary;
        private set
        {
            _mesCapacitySummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesPersistenceSummary = string.Empty;
    public string MesPersistenceSummary
    {
        get => _mesPersistenceSummary;
        private set
        {
            _mesPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastAttemptSummary = string.Empty;
    public string MesLastAttemptSummary
    {
        get => _mesLastAttemptSummary;
        private set
        {
            _mesLastAttemptSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastSuccessSummary = string.Empty;
    public string MesLastSuccessSummary
    {
        get => _mesLastSuccessSummary;
        private set
        {
            _mesLastSuccessSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastFailureSummary = string.Empty;
    public string MesLastFailureSummary
    {
        get => _mesLastFailureSummary;
        private set
        {
            _mesLastFailureSummary = value;
            OnPropertyChanged();
        }
    }

    private string _contextPersistenceSummary = string.Empty;
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
        IEdgeSyncDiagnosticsQuery syncDiagnosticsQuery,
        IAppLanguageService languageService,
        IDiagnosticsModuleDisplayNameResolver displayNameResolver,
        IDiagnosticsSummaryBuilder summaryBuilder,
        IDiagnosticsRowsBuilder rowsBuilder,
        IDiagnosticsInitialSummaryFactory initialSummaryFactory,
        IDiagnosticsRefreshCoordinator refreshCoordinator,
        IDiagnosticsDeadLetterOperator deadLetterOperator,
        IDiagnosticsDeadLetterConfirmationService deadLetterConfirmationService,
        IClientPermissionService permissionService)
        : base(languageService, CoreViewIds.Diagnostics, "Navigation_Menu_CoreDiagnostics", "系统诊断")
    {
        _diagnosticsStore = diagnosticsStore;
        _syncDiagnosticsQuery = syncDiagnosticsQuery;

        _displayNameResolver = displayNameResolver;
        _summaryBuilder = summaryBuilder;
        _rowsBuilder = rowsBuilder;
        _initialSummaryFactory = initialSummaryFactory;
        _refreshCoordinator = refreshCoordinator;
        _deadLetterOperator = deadLetterOperator;
        _deadLetterConfirmationService = deadLetterConfirmationService;
        _permissionService = permissionService;

        _requeueDeadLetterCommand = new AsyncCommand<DeadLetterRow>(RequeueDeadLetterAsync, CanOperateDeadLetter);
        _deleteDeadLetterCommand = new AsyncCommand<DeadLetterRow>(DeleteDeadLetterAsync, CanOperateDeadLetter);
        RequeueDeadLetterCommand = _requeueDeadLetterCommand;
        DeleteDeadLetterCommand = _deleteDeadLetterCommand;
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
        InitializeTabs(languageService);

        ApplyInitialSummaries();
    }

    private void InitializeTabs(IAppLanguageService languageService)
    {
        Tabs.Add(new(languageService, "Diag.Overview", "Navigation_Diagnostics_TabOverview", "系统概况"));
        Tabs.Add(new(languageService, "Diag.SyncOps", "Navigation_Diagnostics_TabSyncOps", "同步运维"));
        Tabs.Add(new(languageService, "Diag.Startup", "Navigation_Diagnostics_TabStartup", "启动诊断"));
        SelectTab(Tabs[0]);
    }

    private void SelectTab(DiagnosticsTabItemViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        foreach (var item in Tabs)
        {
            item.IsSelected = ReferenceEquals(item, tab);
        }

        if (ReferenceEquals(_selectedTab, tab))
        {
            return;
        }

        _selectedTab = tab;
        OnPropertyChanged(nameof(SelectedTab));
        OnPropertyChanged(nameof(IsOverviewTabSelected));
        OnPropertyChanged(nameof(IsSyncOpsTabSelected));
        OnPropertyChanged(nameof(IsStartupTabSelected));
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        foreach (var tab in Tabs)
        {
            tab.RefreshLanguage();
        }
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

    private void StartObserving()
    {
        if (_isObserving)
        {
            return;
        }

        _refreshTimer.Tick += OnRefreshTimerTick;
        _permissionService.PermissionStateChanged += HandlePermissionStateChanged;
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
        _permissionService.PermissionStateChanged -= HandlePermissionStateChanged;
        _isObserving = false;
    }

    internal Task RefreshAsync(CancellationToken ct = default)
        => _refreshCoordinator.RunIfIdleAsync(RefreshCoreAsync, ct);

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

        ApplySummary(_summaryBuilder.Build(report, syncDiagnostics, moduleNameMap));
        ApplyRows(_rowsBuilder.Build(report, syncDiagnostics, moduleNameMap));

        DiscoveredModuleCount = report.DiscoveredModules.Count;
        EnabledModuleCount = report.EnabledModules.Count;
        ActivatedModuleCount = report.ActivatedModules.Count;
    }

    private void ApplyInitialSummaries()
        => ApplySummary(_initialSummaryFactory.Create());

    private void ApplySummary(DiagnosticsSummarySnapshot summary)
    {
        DiscoveredModulesSummary = summary.DiscoveredModulesSummary;
        EnabledModulesSummary = summary.EnabledModulesSummary;
        ActivatedModulesSummary = summary.ActivatedModulesSummary;
        ConfigurationProfileSummary = summary.ConfigurationProfileSummary;
        LastUpdatedSummary = summary.LastUpdatedSummary;
        DeviceSummary = summary.DeviceSummary;
        CloudGateSummary = summary.CloudGateSummary;
        CloudRuntimeSummary = summary.CloudRuntimeSummary;
        CloudResultSummary = summary.CloudResultSummary;
        CloudPendingSummary = summary.CloudPendingSummary;
        CloudCapacitySummary = summary.CloudCapacitySummary;
        CloudPersistenceSummary = summary.CloudPersistenceSummary;
        CloudLastAttemptSummary = summary.CloudLastAttemptSummary;
        CloudLastSuccessSummary = summary.CloudLastSuccessSummary;
        CloudLastFailureSummary = summary.CloudLastFailureSummary;
        MesRuntimeSummary = summary.MesRuntimeSummary;
        MesPendingSummary = summary.MesPendingSummary;
        MesCapacitySummary = summary.MesCapacitySummary;
        MesPersistenceSummary = summary.MesPersistenceSummary;
        MesLastAttemptSummary = summary.MesLastAttemptSummary;
        MesLastSuccessSummary = summary.MesLastSuccessSummary;
        MesLastFailureSummary = summary.MesLastFailureSummary;
        ContextPersistenceSummary = summary.ContextPersistenceSummary;

        if (!string.IsNullOrWhiteSpace(summary.StartupStatusMessage))
        {
            SetStatus(summary.StartupStatusMessage);
        }
    }

    private void ApplyRows(DiagnosticsRowsSnapshot rows)
    {
        ReplaceItems(ModuleRegistrations, rows.ModuleRegistrations);
        ReplaceItems(PluginStates, rows.PluginStates);
        ReplaceItems(DeviceBindings, rows.DeviceBindings);
        ReplaceItems(Issues, rows.Issues);
        ReplaceItems(MesUploadDiagnostics, rows.MesUploadDiagnostics);
        ReplaceItems(SyncChannels, rows.SyncChannels);
        ReplaceItems(CloudDeadLetters, rows.CloudDeadLetters);
        ReplaceItems(MesDeadLetters, rows.MesDeadLetters);

        CloudDeadLetterCount = CloudDeadLetters.Count;
        MesDeadLetterCount = MesDeadLetters.Count;
        TotalIssueCount = Issues.Count;
    }

    private bool CanOperateDeadLetter(DeadLetterRow? row)
        => CanOperateDeadLetters && _deadLetterOperator.CanOperate(row);

    private async Task RequeueDeadLetterAsync(DeadLetterRow row)
    {
        try
        {
            if (!EnsureCanOperateDeadLetters())
            {
                return;
            }

            if (!await _deadLetterConfirmationService.ConfirmRequeueAsync(row))
            {
                SetStatus(GetText("Navigation_Diagnostics_RequeueCanceled", "已取消死信重新入队。"));
                return;
            }

            var result = await _deadLetterOperator.RequeueAsync(row);
            if (result.IsSuccess)
            {
                await RefreshAsync();
                SetStatus(result.Message);
                return;
            }

            SetError(result.Message);
        }
        catch (Exception ex)
        {
            SetError(FormatText(
                "Navigation_Diagnostics_RequeueFailedFormat",
                "死信重新入队失败：{0}",
                ex.Message));
        }
    }

    private async Task DeleteDeadLetterAsync(DeadLetterRow row)
    {
        try
        {
            if (!EnsureCanOperateDeadLetters())
            {
                return;
            }

            if (!await _deadLetterConfirmationService.ConfirmDeleteAsync(row))
            {
                SetStatus(GetText("Navigation_Diagnostics_DeleteCanceled", "已取消死信删除。"));
                return;
            }

            var result = await _deadLetterOperator.DeleteAsync(row);
            if (result.IsSuccess)
            {
                await RefreshAsync();
                SetStatus(result.Message);
                return;
            }

            SetError(result.Message);
        }
        catch (Exception ex)
        {
            SetError(FormatText(
                "Navigation_Diagnostics_DeleteFailedFormat",
                "死信删除失败：{0}",
                ex.Message));
        }
    }

    private bool EnsureCanOperateDeadLetters()
    {
        if (CanOperateDeadLetters)
        {
            return true;
        }

        SetError(GetText(
            "Navigation_Diagnostics_AdminRequired",
            "当前账号不是本地管理员，不能执行死信运维操作。"));
        return false;
    }

    private void HandlePermissionStateChanged()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            RefreshPermissionState();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPermissionState);
    }

    private void RefreshPermissionState()
    {
        OnPropertyChanged(nameof(CanOperateDeadLetters));
        _requeueDeadLetterCommand.RaiseCanExecuteChanged();
        _deleteDeadLetterCommand.RaiseCanExecuteChanged();
    }
}

public sealed class DiagnosticsTabItemViewModel : BaseNotifyPropertyChanged
{
    private readonly IAppLanguageService _languageService;
    private bool _isSelected;

    public DiagnosticsTabItemViewModel(
        IAppLanguageService languageService,
        string key,
        string titleResourceKey,
        string titleFallback)
    {
        _languageService = languageService;
        Key = key;
        TitleResourceKey = titleResourceKey;
        TitleFallback = titleFallback;
    }

    public string Key { get; }

    public string TitleResourceKey { get; }

    public string TitleFallback { get; }

    public string Title => _languageService.GetString(TitleResourceKey, TitleFallback);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public void RefreshLanguage()
        => OnPropertyChanged(nameof(Title));

    public override string ToString()
        => Title;
}
