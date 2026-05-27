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

public class MonitorViewModel : NavigationViewModelBase
{
    private readonly IMonitorViewService _monitorViewService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly LocalizedSyncDiagnosticsText _diagnosticsText;

    private IReadOnlyList<DeviceMonitorSnapshot> _lastSnapshots = [];
    private MonitorTabItemViewModel _selectedTab = null!;
    private string? _selectedDevice;
    private string _cloudSyncStatus = string.Empty;
    private string _mesSyncStatus = string.Empty;
    private string _contextPersistenceStatus = string.Empty;
    private IReadOnlyList<MonitorCellDebugSnapshot> _cellDebugRows = [];
    private MonitorCellDebugItemViewModel? _selectedCell;
    private string _cellQueryText = string.Empty;
    private DataTable _cellTable = new();

    public MonitorViewModel(IMonitorViewService monitorViewService, IAppLanguageService languageService)
        : this(
            monitorViewService,
            languageService,
            "Production.Monitor",
            "Navigation_Title_RealtimeMonitor",
            "实时监控")
    {
    }

    public MonitorViewModel(
        IMonitorViewService monitorViewService,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _monitorViewService = monitorViewService;
        _diagnosticsText = new LocalizedSyncDiagnosticsText(languageService);

        Tabs =
        [
            new(languageService, "DeviceStatus", "Navigation_Monitor_Tab_DeviceStatus", "设备状态"),
            new(languageService, "StateMachine", "Navigation_Monitor_Tab_StateMachine", "状态机")
        ];

        SelectTabCommand = new BaseCommand(parameter =>
        {
            if (parameter is MonitorTabItemViewModel tab)
            {
                Select(tab);
            }
        });
        Select(Tabs[0]);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += (_, _) => RunViewTaskInBackground(
            RefreshAsync,
            GetText("Navigation_Monitor_RefreshFailed", "监控刷新失败。"));
    }

    public ObservableCollection<MonitorTabItemViewModel> Tabs { get; }

    public MonitorTabItemViewModel SelectedTab
    {
        get => _selectedTab;
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

    public bool IsDeviceStatusTabSelected => SelectedTab.Key == "DeviceStatus";

    public bool IsStateMachineTabSelected => SelectedTab.Key == "StateMachine";

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
            RefreshAsync,
            GetText("Navigation_Monitor_LoadFailed", "加载监控数据失败。"));
    }

    public override Task OnDeactivatedAsync()
    {
        _refreshTimer.Stop();
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        ApplySnapshots(await _monitorViewService.GetSnapshotsAsync());
    }

    private void ApplySnapshots(IReadOnlyList<DeviceMonitorSnapshot> snapshots)
    {
        _lastSnapshots = snapshots;
        ReplaceItems(DeviceOptions, snapshots.Select(static snapshot => snapshot.DeviceName).Distinct());

        var nextDevice = ResolveSelectedDevice(snapshots);
        if (!string.Equals(_selectedDevice, nextDevice, StringComparison.Ordinal))
        {
            _selectedDevice = nextDevice;
            OnPropertyChanged(nameof(SelectedDevice));
        }

        ApplySelectedSnapshot();
    }

    private string? ResolveSelectedDevice(IReadOnlyList<DeviceMonitorSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_selectedDevice)
            && snapshots.Any(snapshot => string.Equals(snapshot.DeviceName, _selectedDevice, StringComparison.Ordinal)))
        {
            return _selectedDevice;
        }

        return snapshots[0].DeviceName;
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
            ReplaceItems(StateMachineTaskItems, []);
            _cellDebugRows = [];
            ApplyCellDebugFilter();
            _cellTable = new DataTable();
            RefreshSyncDiagnostics(null);
            RaiseSnapshotPropertiesChanged();
            return;
        }

        var summaryItems = CreateSummaryItems(snapshot);
        ReplaceItems(PrimarySummaryItems, summaryItems.Take(Math.Max(0, summaryItems.Count - 1)));
        ReplaceItems(StateMachineSummaryItems, CreateStateMachineSummaryItems(summaryItems));
        LastErrorItem = summaryItems[^1];
        ReplaceItems(EquipmentStatusRows, snapshot.EquipmentStatusRows);
        ReplaceItems(RealtimeRows, snapshot.RealtimeRows);
        ReplaceItems(StepRows, snapshot.StepRows);
        ReplaceItems(DeviceDataRows, snapshot.DeviceDataRows);
        ReplaceItems(
            StateMachineTaskItems,
            snapshot.StateMachineTaskRows
                .Where(static row => row.Enabled)
                .Select(row => MonitorStateMachineTaskItemViewModel.Create(row, GetText)));
        _cellDebugRows = snapshot.CellDebugRows;
        ApplyCellDebugFilter();
        _cellTable = snapshot.CellTable;
        RefreshSyncDiagnostics(snapshot);
        RaiseSnapshotPropertiesChanged();
    }

    private DeviceMonitorSnapshot? FindSelectedSnapshot()
        => string.IsNullOrWhiteSpace(_selectedDevice)
            ? null
            : _lastSnapshots.FirstOrDefault(snapshot =>
                string.Equals(snapshot.DeviceName, _selectedDevice, StringComparison.Ordinal));

    private void ApplyCellDebugFilter()
    {
        var selectedKey = SelectedCell?.InternalKey;
        var filteredRows = FilterCellDebugRows(_cellDebugRows, _cellQueryText)
            .Select(static row => new MonitorCellDebugItemViewModel(row))
            .ToList();

        ReplaceItems(CellDebugItems, filteredRows);
        var nextSelectedCell = !string.IsNullOrWhiteSpace(selectedKey)
            ? CellDebugItems.FirstOrDefault(item => string.Equals(item.InternalKey, selectedKey, StringComparison.Ordinal))
            : null;
        SetSelectedCell(nextSelectedCell ?? CellDebugItems.FirstOrDefault());
        RaiseCellDebugPropertiesChanged();
    }

    private static IEnumerable<MonitorCellDebugSnapshot> FilterCellDebugRows(
        IReadOnlyList<MonitorCellDebugSnapshot> rows,
        string queryText)
    {
        var query = queryText.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return rows;
        }

        return rows.Where(row =>
            Contains(row.InternalKey, query)
            || Contains(row.DisplayLabel, query));
    }

    private static bool Contains(string value, string query)
        => value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

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

    private IReadOnlyList<MonitorStatusItemVm> CreateSummaryItems(DeviceMonitorSnapshot snapshot)
        =>
        [
            new(
                GetText("Navigation_Monitor_ColumnConnectionStatus", "连接状态"),
                snapshot.IsConnected
                    ? GetText("Navigation_Monitor_ConnectionOnline", "已连接")
                    : GetText("Navigation_Monitor_ConnectionOffline", "未连接")),
            new(GetText("Navigation_Monitor_Source", "数据来源"), FormatSource(snapshot.Source)),
            new(
                GetText("Navigation_Monitor_ConfigurationState", "配置状态"),
                FormatConfigurationState(snapshot)),
            new(GetText("Navigation_Monitor_ConfigurationEndpoint", "PLC 端点"), snapshot.PlcEndpointText),
            new(GetText("Navigation_Monitor_LastHeartbeat", "最近心跳"), snapshot.LastHeartbeatText),
            new(GetText("Navigation_Monitor_LastUpdated", "最近更新"), snapshot.LastUpdatedText),
            new(GetText("Navigation_Monitor_WipCount", "在制记录"), snapshot.CellCount.ToString()),
            new(GetText("Navigation_Monitor_LastConnected", "最近连接"), snapshot.LastConnectedAtText),
            new(GetText("Navigation_Monitor_LastFailure", "最近异常"), snapshot.LastFailureAtText),
            new(GetText("Navigation_Monitor_LastError", "最后错误"), snapshot.LastErrorText)
        ];

    private static IReadOnlyList<MonitorStatusItemVm> CreateStateMachineSummaryItems(IReadOnlyList<MonitorStatusItemVm> summaryItems)
        => summaryItems.Count < 7
            ? summaryItems.Take(Math.Max(0, summaryItems.Count - 1)).ToList()
            : [summaryItems[0], summaryItems[1], summaryItems[2], summaryItems[3], summaryItems[6]];

    private string FormatSource(MonitorSnapshotSource source)
        => source switch
        {
            MonitorSnapshotSource.ProductionContext => GetText(
                "Navigation_Monitor_SourceProductionContext",
                "生产上下文"),
            MonitorSnapshotSource.RuntimeStatus => GetText(
                "Navigation_Monitor_SourceRuntimeStatus",
                "PLC 运行状态"),
            MonitorSnapshotSource.PlcConfiguration => GetText(
                "Navigation_Monitor_SourcePlcConfiguration",
                "PLC 配置"),
            _ => GetText("Navigation_Monitor_SourceUnknown", "未知")
        };

    private string FormatConfigurationState(DeviceMonitorSnapshot snapshot)
    {
        if (!snapshot.HasPlcConfiguration)
        {
            return GetText("Navigation_Monitor_ConfigurationMissing", "未配置");
        }

        return snapshot.IsPlcConfigurationEnabled
            ? GetText("Navigation_Monitor_ConfigurationEnabled", "已启用")
            : GetText("Navigation_Monitor_ConfigurationDisabled", "未启用");
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
        OnPropertyChanged(nameof(IsDeviceStatusTabSelected));
        OnPropertyChanged(nameof(IsStateMachineTabSelected));
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        foreach (var tab in Tabs)
        {
            tab.RefreshLanguage();
        }

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
}
