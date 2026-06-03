using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

public class MonitorViewModel : NavigationViewModelBase, IMonitorViewModelCallback
{
    private readonly IMonitorSnapshotQueryFacade _monitorSnapshotQueryFacade;
    private readonly DispatcherTimer _refreshTimer;
    private readonly LocalizedSyncDiagnosticsText _diagnosticsText;
    private readonly IMonitorViewModelSummaryFormatter _summaryFormatter;
    private readonly IMonitorStateMachineTaskItemFactory _stateMachineTaskItemFactory;
    private readonly IMonitorViewModelTabController _tabController;

    private IReadOnlyList<DeviceMonitorSnapshot> _lastSnapshots = [];
    private string? _selectedDevice;
    private string _cloudSyncStatus = string.Empty;
    private string _mesSyncStatus = string.Empty;
    private string _contextPersistenceStatus = string.Empty;
    private IReadOnlyList<MonitorCellDebugSnapshot> _cellDebugRows = [];
    private MonitorCellDebugItemViewModel? _selectedCell;
    private string _cellQueryText = string.Empty;
    private DataTable _cellTable = new();
    private bool _refreshInFlight;

    public MonitorViewModel(
        IMonitorSnapshotQueryFacade monitorSnapshotQueryFacade,
        IAppLanguageService languageService,
        IMonitorViewModelCollaboratorFactory collaboratorFactory)
        : this(
            monitorSnapshotQueryFacade,
            languageService,
            collaboratorFactory,
            "Production.Monitor",
            "Navigation_Title_RealtimeMonitor",
            "实时监控")
    {
    }

    public MonitorViewModel(
        IMonitorSnapshotQueryFacade monitorSnapshotQueryFacade,
        IAppLanguageService languageService,
        IMonitorViewModelCollaboratorFactory collaboratorFactory,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _monitorSnapshotQueryFacade = monitorSnapshotQueryFacade;
        _diagnosticsText = new LocalizedSyncDiagnosticsText(languageService);
        Tabs = [];
        var collaborators = collaboratorFactory.Create(new MonitorViewModelCollaboratorContext(this, Tabs));
        _tabController = collaborators.TabController;
        _summaryFormatter = collaborators.SummaryFormatter;
        _stateMachineTaskItemFactory = collaborators.StateMachineTaskItemFactory;

        SelectTabCommand = new BaseCommand(parameter =>
        {
            if (parameter is MonitorTabItemViewModel tab)
            {
                Select(tab);
            }
        });
        _tabController.Initialize();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += (_, _) => RunViewTaskInBackground(
            RefreshOnceAsync,
            GetText("Navigation_Monitor_RefreshFailed", "监控刷新失败。"));
    }

    public ObservableCollection<MonitorTabItemViewModel> Tabs { get; }

    public MonitorTabItemViewModel SelectedTab
    {
        get => _tabController.SelectedTab;
        set => Select(value);
    }

    public ICommand SelectTabCommand { get; }

    public ObservableCollection<string> DeviceOptions { get; } = [];

    public string? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (string.Equals(_selectedDevice, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedDevice = value;
            OnPropertyChanged();
            ApplySelectedSnapshot();
        }
    }

    private MonitorStatusItemVm _lastErrorItem = new(string.Empty, string.Empty);

    public ObservableCollection<MonitorStatusItemVm> PrimarySummaryItems { get; } = [];

    public ObservableCollection<MonitorStatusItemVm> StateMachineSummaryItems { get; } = [];

    public ObservableCollection<MonitorStateMachineTaskItemViewModel> StateMachineTaskItems { get; } = [];
    public ObservableCollection<MonitorStateMachineTaskItemViewModel> StateMachineHeartbeatTaskItems { get; } = [];
    public ObservableCollection<MonitorStateMachineTaskItemViewModel> StateMachineRuntimeTaskItems { get; } = [];

    public MonitorStatusItemVm LastErrorItem
    {
        get => _lastErrorItem;
        private set
        {
            _lastErrorItem = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<MonitorSnapshotRow> EquipmentStatusRows { get; } = [];

    public ObservableCollection<MonitorSnapshotRow> RealtimeRows { get; } = [];

    public ObservableCollection<MonitorSnapshotRow> StepRows { get; } = [];

    public ObservableCollection<MonitorSnapshotRow> DeviceDataRows { get; } = [];

    public ObservableCollection<MonitorCellDebugItemViewModel> CellDebugItems { get; } = [];

    public ObservableCollection<MonitorSnapshotRow> SelectedCellFieldRows { get; } = [];

    public string CellQueryText
    {
        get => _cellQueryText;
        set
        {
            if (string.Equals(_cellQueryText, value, StringComparison.Ordinal))
            {
                return;
            }

            _cellQueryText = value;
            OnPropertyChanged();
            ApplyCellDebugFilter();
        }
    }

    public MonitorCellDebugItemViewModel? SelectedCell
    {
        get => _selectedCell;
        set => SetSelectedCell(value);
    }

    public bool IsDeviceStatusTabSelected => _tabController.IsDeviceStatusTabSelected;

    public bool IsStateMachineTabSelected => _tabController.IsStateMachineTabSelected;

    public bool HasDevices => DeviceOptions.Count > 0;

    public bool IsDevicesEmpty => DeviceOptions.Count == 0;

    public int StepRowCount => StepRows.Count;

    public int DeviceDataRowCount => DeviceDataRows.Count;

    public int EquipmentStatusRowCount => EquipmentStatusRows.Count;

    public int RealtimeRowCount => RealtimeRows.Count;

    public int CellRowCount => _cellDebugRows.Count;

    public int FilteredCellCount => CellDebugItems.Count;

    public System.Data.DataView CellRows => _cellTable.DefaultView;

    public bool IsEquipmentStatusRowsEmpty => EquipmentStatusRows.Count == 0;

    public bool IsRealtimeRowsEmpty => RealtimeRows.Count == 0;

    public bool IsStepRowsEmpty => StepRows.Count == 0;

    public bool IsDeviceDataRowsEmpty => DeviceDataRows.Count == 0;

    public bool IsCellRowsEmpty => _cellTable.Rows.Count == 0;

    public bool IsCellDebugEmpty => CellDebugItems.Count == 0;
    public bool HasStateMachineTaskItems => StateMachineTaskItems.Count > 0;
    public bool IsStateMachineTaskItemsEmpty => StateMachineTaskItems.Count == 0;
    public bool HasStateMachineHeartbeatTaskItems => StateMachineHeartbeatTaskItems.Count > 0;
    public bool HasStateMachineRuntimeTaskItems => StateMachineRuntimeTaskItems.Count > 0;
    public bool HasSelectedCell => SelectedCell is not null;

    public bool IsSelectedCellEmpty => SelectedCell is null;

    public bool IsSelectedCellFieldRowsEmpty => SelectedCellFieldRows.Count == 0;

    public string CloudSyncStatus
    {
        get => _cloudSyncStatus;
        private set { _cloudSyncStatus = value; OnPropertyChanged(); }
    }

    public string MesSyncStatus
    {
        get => _mesSyncStatus;
        private set { _mesSyncStatus = value; OnPropertyChanged(); }
    }

    public string ContextPersistenceStatus
    {
        get => _contextPersistenceStatus;
        private set { _contextPersistenceStatus = value; OnPropertyChanged(); }
    }

    public override async Task OnActivatedAsync()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }

        await RunViewTaskAsync(
            RefreshOnceAsync,
            GetText("Navigation_Monitor_LoadFailed", "加载监控数据失败。"));
    }

    public override Task OnDeactivatedAsync()
    {
        _refreshTimer.Stop();
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        ApplySnapshots(await _monitorSnapshotQueryFacade.GetSnapshotsAsync());
    }

    private async Task RefreshOnceAsync()
    {
        if (_refreshInFlight)
        {
            return;
        }

        _refreshInFlight = true;
        try
        {
            await RefreshAsync();
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private void ApplySnapshots(IReadOnlyList<DeviceMonitorSnapshot> snapshots)
    {
        _lastSnapshots = snapshots;
        ReplaceItems(DeviceOptions, snapshots.Select(static snapshot => snapshot.DeviceName).Distinct());

        var nextDevice = MonitorViewModelSnapshotApplier.ResolveSelectedDevice(snapshots, _selectedDevice);
        if (!string.Equals(_selectedDevice, nextDevice, StringComparison.Ordinal))
        {
            _selectedDevice = nextDevice;
            OnPropertyChanged(nameof(SelectedDevice));
        }

        ApplySelectedSnapshot();
    }

    private void ApplySelectedSnapshot()
    {
        var snapshot = FindSelectedSnapshot();
        if (snapshot is null)
        {
            ReplaceItems(PrimarySummaryItems, []);
            ReplaceItems(StateMachineSummaryItems, []);
            LastErrorItem = new(string.Empty, string.Empty);
            ReplaceItems(EquipmentStatusRows, []);
            ReplaceItems(RealtimeRows, []);
            ReplaceItems(StepRows, []);
            ReplaceItems(DeviceDataRows, []);
            ApplyStateMachineTaskItems([]);
            _cellDebugRows = [];
            ApplyCellDebugFilter();
            _cellTable = new DataTable();
            RefreshSyncDiagnostics(null);
            RaiseSnapshotPropertiesChanged();
            return;
        }

        var summaryItems = _summaryFormatter.CreateSummaryItems(snapshot);
        ReplaceItems(PrimarySummaryItems, summaryItems.Take(Math.Max(0, summaryItems.Count - 1)));
        ReplaceItems(StateMachineSummaryItems, _summaryFormatter.CreateStateMachineSummaryItems(summaryItems));
        LastErrorItem = summaryItems[^1];
        ReplaceItems(EquipmentStatusRows, snapshot.EquipmentStatusRows);
        ReplaceItems(RealtimeRows, snapshot.RealtimeRows);
        ReplaceItems(StepRows, snapshot.StepRows);
        ReplaceItems(DeviceDataRows, snapshot.DeviceDataRows);
        ApplyStateMachineTaskItems(_stateMachineTaskItemFactory.CreateItems(snapshot.StateMachineTaskRows));
        _cellDebugRows = snapshot.CellDebugRows;
        ApplyCellDebugFilter();
        _cellTable = snapshot.CellTable;
        RefreshSyncDiagnostics(snapshot);
        RaiseSnapshotPropertiesChanged();
    }

    private DeviceMonitorSnapshot? FindSelectedSnapshot()
        => MonitorViewModelSnapshotApplier.FindSelectedSnapshot(_lastSnapshots, _selectedDevice);

    private void ApplyStateMachineTaskItems(IReadOnlyList<MonitorStateMachineTaskItemViewModel> items)
    {
        ReplaceItems(StateMachineTaskItems, items);
        ReplaceItems(StateMachineHeartbeatTaskItems, items.Where(static x => x.IsHeartbeatLike));
        ReplaceItems(StateMachineRuntimeTaskItems, items.Where(static x => !x.IsHeartbeatLike));
    }

    private void ApplyCellDebugFilter()
    {
        var selectedKey = SelectedCell?.InternalKey;
        var filteredRows = MonitorViewModelCellDebugController.CreateFilteredItems(_cellDebugRows, _cellQueryText);

        ReplaceItems(CellDebugItems, filteredRows);
        SetSelectedCell(MonitorViewModelCellDebugController.ResolveSelectedCell(CellDebugItems, selectedKey));
        RaiseCellDebugPropertiesChanged();
    }

    private void SetSelectedCell(MonitorCellDebugItemViewModel? cell)
    {
        if (ReferenceEquals(_selectedCell, cell))
        {
            return;
        }

        _selectedCell = cell;
        OnPropertyChanged(nameof(SelectedCell));
        ReplaceItems(SelectedCellFieldRows, cell?.FieldRows ?? []);
        RaiseSelectedCellPropertiesChanged();
    }

    private void RefreshSyncDiagnostics(DeviceMonitorSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            CloudSyncStatus = GetText("Navigation_Monitor_CloudUnknown", "云端状态未知");
            MesSyncStatus = GetText("Navigation_Monitor_MesUnknown", "MES 状态未知");
            ContextPersistenceStatus = GetText("Navigation_Monitor_ContextPersistenceOk", "损坏文件数：0");
            return;
        }

        CloudSyncStatus = _diagnosticsText.FormatCloudMonitorSummary(snapshot.CloudSync);
        MesSyncStatus = _diagnosticsText.FormatMesMonitorSummary(snapshot.MesSync);
        ContextPersistenceStatus = _diagnosticsText.FormatContextPersistenceSummary(snapshot.ContextPersistence);
    }

    private void Select(MonitorTabItemViewModel? tab)
    {
        _tabController.Select(tab);
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        _tabController.RefreshLanguage();
        ApplySelectedSnapshot();
    }

    private void RaiseSnapshotPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(IsDevicesEmpty));
        OnPropertyChanged(nameof(StepRowCount));
        OnPropertyChanged(nameof(DeviceDataRowCount));
        OnPropertyChanged(nameof(EquipmentStatusRowCount));
        OnPropertyChanged(nameof(RealtimeRowCount));
        OnPropertyChanged(nameof(CellRowCount));
        OnPropertyChanged(nameof(FilteredCellCount));
        OnPropertyChanged(nameof(CellRows));
        OnPropertyChanged(nameof(IsEquipmentStatusRowsEmpty));
        OnPropertyChanged(nameof(IsRealtimeRowsEmpty));
        OnPropertyChanged(nameof(IsStepRowsEmpty));
        OnPropertyChanged(nameof(IsDeviceDataRowsEmpty));
        OnPropertyChanged(nameof(HasStateMachineTaskItems));
        OnPropertyChanged(nameof(IsStateMachineTaskItemsEmpty));
        OnPropertyChanged(nameof(HasStateMachineHeartbeatTaskItems));
        OnPropertyChanged(nameof(HasStateMachineRuntimeTaskItems));
        OnPropertyChanged(nameof(IsCellRowsEmpty));
        OnPropertyChanged(nameof(IsCellDebugEmpty));
        OnPropertyChanged(nameof(IsSelectedCellFieldRowsEmpty));
        RaiseSelectedCellPropertiesChanged();
    }

    private void RaiseCellDebugPropertiesChanged()
    {
        OnPropertyChanged(nameof(FilteredCellCount));
        OnPropertyChanged(nameof(IsCellDebugEmpty));
        OnPropertyChanged(nameof(IsSelectedCellFieldRowsEmpty));
    }

    private void RaiseSelectedCellPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasSelectedCell));
        OnPropertyChanged(nameof(IsSelectedCellEmpty));
        OnPropertyChanged(nameof(IsSelectedCellFieldRowsEmpty));
    }

    void IMonitorViewModelCallback.NotifyPropertyChanged(string propertyName)
        => OnPropertyChanged(propertyName);
}
