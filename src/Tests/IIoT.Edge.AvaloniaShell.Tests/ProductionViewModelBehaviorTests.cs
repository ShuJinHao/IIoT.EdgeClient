using IIoT.Edge.Application.Abstractions.Auth;
using System.Data;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Presentation.Navigation.Avalonia.Localization;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class ProductionViewModelBehaviorTests
{
    [Fact]
    public async Task Monitor_view_model_loads_snapshots_through_service_and_timer()
    {
        var service = new FakeMonitorViewService
        {
            Snapshots = [CreateMonitorSnapshot("PLC-A", 10)]
        };
        var timerFactory = new FakeAvaloniaTimerFactory();
        var viewModel = new MonitorViewModel(
            service,
            CreateLanguageService(),
            timerFactory,
            "test.monitor",
            "Navigation_Title_RealtimeMonitor",
            "实时监控");

        await viewModel.OnActivatedAsync();

        Assert.True(timerFactory.LastTimer?.IsEnabled);
        Assert.Equal(1, service.CallCount);
        var firstRow = Assert.Single(viewModel.Devices);
        Assert.Equal("PLC-A", firstRow.DeviceName);
        Assert.Equal(10, firstRow.TotalAll);
        Assert.Contains("待处理", firstRow.CloudSyncStatus, StringComparison.Ordinal);

        service.Snapshots = [CreateMonitorSnapshot("PLC-B", 20)];
        timerFactory.LastTimer?.RaiseTick();

        Assert.Equal(2, service.CallCount);
        var refreshedRow = Assert.Single(viewModel.Devices);
        Assert.Equal("PLC-B", refreshedRow.DeviceName);
        Assert.Equal(20, refreshedRow.TotalAll);

        await viewModel.OnDeactivatedAsync();

        Assert.False(timerFactory.LastTimer?.IsEnabled);
    }

    [Fact]
    public async Task Data_view_model_queries_summary_and_records_through_service()
    {
        var service = new FakeDataViewService();
        var viewModel = new DataViewModel(
            service,
            CreateLanguageService(),
            "test.data",
            "Navigation_Title_Data",
            "生产数据")
        {
            DateFrom = new DateTime(2026, 5, 12),
            DateTo = new DateTime(2026, 5, 13)
        };

        await viewModel.OnActivatedAsync();

        Assert.Equal(new DateTime(2026, 5, 12), service.LastDateFrom);
        Assert.Equal(new DateTime(2026, 5, 13), service.LastDateTo);
        Assert.Equal(128, viewModel.TodayTotal);
        Assert.Equal(126, viewModel.TodayOk);
        Assert.Equal(2, viewModel.TodayNg);
        Assert.Equal("98.44%", viewModel.TodayYield);
        var row = Assert.Single(viewModel.Records);
        Assert.Equal("B-001", row.BatchNo);
        Assert.Equal(63, row.Ok);

        viewModel.ExportCommand.Execute(null);

        Assert.Contains("不写出导出文件", viewModel.FeedbackMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capacity_view_model_loads_and_queries_capacity_through_service()
    {
        var service = new FakeCapacityViewService
        {
            IsOnline = true,
            TodayResult = CreateCapacityResult("08:00", 32),
            HistoryResult = CreateCapacityResult("2026-05-13", 64)
        };
        var viewModel = new CapacityViewModel(
            service,
            CreateLanguageService(),
            new FakeAvaloniaDialogService(),
            new ImmediateAvaloniaDispatcherService(),
            "test.capacity",
            "Navigation_Title_CapacityQuery",
            "产能查询")
        {
            QueryDate = new DateTime(2026, 5, 13),
            SelectedQueryMode = CapacityQueryModes.Month
        };

        await viewModel.OnActivatedAsync();

        Assert.True(viewModel.IsOnline);
        Assert.Equal("PLC-A", viewModel.SelectedDeviceName);
        Assert.Equal(1, service.LoadTodayCalls);
        Assert.Equal(32, viewModel.PeriodTotal);
        Assert.Single(viewModel.Records);

        await viewModel.QueryCommand.ExecuteAsync(null);

        Assert.Equal(1, service.QueryHistoryCalls);
        Assert.Equal(CapacityQueryModes.Month, service.LastQueryMode);
        Assert.Equal(new DateTime(2026, 5, 13), service.LastQueryDate);
        Assert.Equal("PLC-A", service.LastQueryPlcName);
        Assert.Equal(64, viewModel.PeriodTotal);
    }

    [Fact]
    public async Task Capacity_view_model_shows_avalonia_dialog_when_offline()
    {
        var service = new FakeCapacityViewService { IsOnline = false };
        var dialogService = new FakeAvaloniaDialogService();
        var viewModel = new CapacityViewModel(
            service,
            CreateLanguageService(),
            dialogService,
            new ImmediateAvaloniaDispatcherService(),
            "test.capacity",
            "Navigation_Title_CapacityQuery",
            "产能查询");

        await viewModel.OnActivatedAsync();
        await viewModel.QueryCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanQueryCloud);
        Assert.Equal(0, service.QueryHistoryCalls);
        var request = Assert.Single(dialogService.Requests);
        Assert.Equal(AvaloniaDialogRequestKind.Info, request.Kind);
        Assert.Contains("暂时无法查询云端产能", request.Message, StringComparison.Ordinal);

        service.RaiseUploadGate(EdgeUploadGateState.Ready);

        Assert.True(viewModel.IsOnline);
        Assert.True(service.LoadTodayCalls > 0);
    }

    [Fact]
    public async Task Plc_task_binding_view_model_confirms_before_disabling_heartbeat_task()
    {
        var service = new FakePlcTaskBindingService();
        var dialogService = new FakeAvaloniaDialogService();
        var viewModel = new PlcTaskBindingViewModel(
            service,
            new FakeClientPermissionService { CanEditHardware = true },
            CreateLanguageService(),
            dialogService,
            new ImmediateAvaloniaDispatcherService(),
            "Homogenization.PlcTaskBindingView",
            "Navigation_Menu_PlcTaskBinding",
            "PLC 任务绑定");

        await viewModel.OnActivatedAsync();

        var device = Assert.Single(viewModel.Devices);
        Assert.Equal("PLC-A", device.DeviceName);
        Assert.True(viewModel.CanSave);

        var heartbeatTask = Assert.Single(device.Tasks, static task => task.IsHeartbeatLike);
        heartbeatTask.Enabled = false;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogService.Requests, static request => request.Kind == AvaloniaDialogRequestKind.Confirm);
        Assert.Contains("确认禁用心跳任务", request.Title, StringComparison.Ordinal);
        Assert.Equal(7, service.LastSavedNetworkDeviceId);
        Assert.Equal("Homogenization", service.LastSavedModuleId);
        Assert.False(service.LastSavedTaskStates["Heartbeat"]);
    }

    [Fact]
    public async Task Diagnostics_view_model_reads_registration_and_persistence_snapshots_without_actions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStartupDiagnosticsStore>(new FakeStartupDiagnosticsStore(CreateStartupDiagnosticsReport()));
        services.AddSingleton<IEdgeSyncDiagnosticsQuery>(new FakeEdgeSyncDiagnosticsQuery());
        services.AddSingleton<IStationRuntimeRegistry>(new FakeStationRuntimeRegistry());
        var provider = services.BuildServiceProvider();

        var viewModel = new DiagnosticsViewModel(
            provider,
            CreateLanguageService(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();

        Assert.Single(viewModel.RuntimeRegistrations);
        Assert.Single(viewModel.ModuleRegistrations);
        Assert.Equal(3, viewModel.PersistenceRows.Count);
        Assert.Single(viewModel.PluginStates);
        Assert.Single(viewModel.Issues);
        Assert.Contains("1 个运行时注册", viewModel.FeedbackMessage, StringComparison.Ordinal);
        Assert.Contains("运行目录：runtime-data", viewModel.ConfigurationProfileText, StringComparison.Ordinal);
    }

    private static IAvaloniaLanguageService CreateLanguageService()
    {
        var service = new AvaloniaResourceLanguageService(
            [
                new NavigationAvaloniaZhCnResources(),
                new NavigationAvaloniaEnUsResources()
            ]);
        service.Apply("zh-CN");
        return service;
    }

    private static DeviceMonitorSnapshot CreateMonitorSnapshot(string deviceName, int total)
    {
        var table = new DataTable();
        table.Columns.Add("TrayCode", typeof(string));
        table.Rows.Add("T-001");

        return new DeviceMonitorSnapshot(
            DeviceName: deviceName,
            DayShiftOk: total - 2,
            DayShiftNg: 1,
            DayShiftTotal: total - 1,
            DayShiftYield: "90.00%",
            NightShiftOk: 1,
            NightShiftNg: 0,
            NightShiftTotal: 1,
            NightShiftYield: "100.00%",
            TotalAll: total,
            OkAll: total - 1,
            NgAll: 1,
            YieldAll: "90.00%",
            DeviceDataSummary: "Speed=10",
            StepSummary: "Running",
            CellCount: 1,
            CellTable: table,
            CloudSync: CreateCloudSnapshot(),
            MesSync: CreateMesSnapshot(),
            ContextPersistence: new ProductionContextPersistenceDiagnostics(0, null));
    }

    private static CloudSyncDiagnosticsSnapshot CreateCloudSnapshot()
        => new(
            EdgeUploadGateState.Ready,
            EdgeUploadBlockReason.None,
            CloudRetryRuntimeState.Idle,
            LastAttemptAt: null,
            LastSuccessAt: null,
            LastFailureAt: null,
            LastOutcome: CloudCallOutcome.Success,
            LastReasonCode: string.Empty,
            LastProcessType: "Homogenization",
            PendingRetryCount: 0,
            PendingDeviceLogCount: 1,
            PendingCapacityCount: 2,
            IsPausedWaitingForRecovery: false,
            IsCapacityBlocked: false,
            BlockedChannel: null,
            BlockedReason: string.Empty,
            LastCapacityBlockAt: null,
            IsPersistenceFaulted: false,
            LastPersistenceFaultAt: null,
            PersistenceFaultMessage: null,
            PendingPassStationCount: 3);

    private static MesSyncDiagnosticsSnapshot CreateMesSnapshot()
        => new(
            MesRetryRuntimeState.Idle,
            LastAttemptAt: null,
            LastSuccessAt: null,
            LastFailureAt: null,
            LastFailureReason: null,
            PendingRetryCount: 0,
            Channels: [],
            IsCapacityBlocked: false,
            BlockedChannel: null,
            BlockedReason: string.Empty,
            LastCapacityBlockAt: null,
            IsPersistenceFaulted: false,
            LastPersistenceFaultAt: null,
            PersistenceFaultMessage: null);

    private static CapacityViewResult CreateCapacityResult(string date, int total)
        => new(
            [
                new DailyCapacityVm
                {
                    Date = date,
                    DateFull = date,
                    Total = total,
                    OkCount = total - 1,
                    NgCount = 1,
                    Yield = "98.44%",
                    DayShiftTotal = total,
                    DayShiftOk = total - 1,
                    DayShiftNg = 1
                }
            ],
            total,
            total - 1,
            1,
            "98.44%",
            total.ToString());

    private static StartupDiagnosticsReport CreateStartupDiagnosticsReport()
        => new(
            new DateTime(2026, 5, 13, 8, 30, 0),
            new ConfigurationProfileSnapshot("Production", "line-a", "line-a.json", true, "runtime-data"),
            ["Homogenization"],
            ["Homogenization"],
            ["Homogenization"],
            [new PluginLifecycleSnapshot("Homogenization", "匀浆", "Homogenization", "1.0.0", PluginLifecycleState.Activated, "已激活")],
            [new ModuleRegistrationSnapshot("Homogenization", "Homogenization", "IIoT.Edge.Module.Homogenization", true, true, true, true, true, true)],
            [],
            [new StartupDiagnosticIssue("TEST", "只读诊断问题", "Homogenization", "PLC-A")]);

    private sealed class FakeMonitorViewService : IMonitorViewService
    {
        public List<DeviceMonitorSnapshot> Snapshots { get; set; } = [];

        public int CallCount { get; private set; }

        public Task<List<DeviceMonitorSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Snapshots);
        }
    }

    private sealed class FakeDataViewService : IDataViewService
    {
        public DateTime LastDateFrom { get; private set; }

        public DateTime LastDateTo { get; private set; }

        public Task<DataViewSnapshot> QueryAsync(
            DateTime dateFrom,
            DateTime dateTo,
            CancellationToken cancellationToken = default)
        {
            LastDateFrom = dateFrom;
            LastDateTo = dateTo;
            return Task.FromResult(new DataViewSnapshot(
                128,
                126,
                2,
                "98.44%",
                [new ProductionRecordItem("08:00", "B-001", 64, 63, 1, "98.44%")]));
        }
    }

    private sealed class FakeCapacityViewService : ICapacityViewService
    {
        public event Action<EdgeUploadGateSnapshot>? UploadGateChanged;

        public bool IsOnline { get; set; }

        public CapacityViewResult TodayResult { get; set; } = CreateCapacityResult("08:00", 0);

        public CapacityViewResult HistoryResult { get; set; } = CreateCapacityResult("2026-05-13", 0);

        public int LoadTodayCalls { get; private set; }

        public int QueryHistoryCalls { get; private set; }

        public string? LastQueryMode { get; private set; }

        public DateTime? LastQueryDate { get; private set; }

        public string? LastQueryPlcName { get; private set; }

        public IReadOnlyList<string> GetDeviceNames()
            => ["PLC-A", "PLC-B"];

        public Task<CapacityViewResult> LoadTodayAsync(
            string plcName,
            CancellationToken cancellationToken = default)
        {
            LoadTodayCalls++;
            return Task.FromResult(TodayResult);
        }

        public Task<CapacityViewResult> QueryHistoryAsync(
            string queryMode,
            DateTime queryDate,
            string plcName,
            CancellationToken cancellationToken = default)
        {
            QueryHistoryCalls++;
            LastQueryMode = queryMode;
            LastQueryDate = queryDate;
            LastQueryPlcName = plcName;
            return Task.FromResult(HistoryResult);
        }

        public void RaiseUploadGate(EdgeUploadGateState state)
        {
            IsOnline = state == EdgeUploadGateState.Ready;
            UploadGateChanged?.Invoke(new EdgeUploadGateSnapshot { State = state });
        }
    }

    private sealed class FakePlcTaskBindingService : IPlcTaskBindingService
    {
        public int LastSavedNetworkDeviceId { get; private set; }

        public string LastSavedModuleId { get; private set; } = string.Empty;

        public IReadOnlyDictionary<string, bool> LastSavedTaskStates { get; private set; } =
            new Dictionary<string, bool>();

        public Task<IReadOnlyList<PlcTaskBindingDeviceDto>> GetModuleDeviceBindingsAsync(
            string moduleId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PlcTaskBindingDeviceDto>>(
            [
                new PlcTaskBindingDeviceDto(
                    7,
                    "PLC-A",
                    moduleId,
                    true,
                    [
                        new PlcTaskBindingItemDto(
                            "Heartbeat",
                            "云端心跳",
                            true,
                            true,
                            true,
                            [new TaskRequiredSignal("CloudHeartbeat", "Write")],
                            true,
                            string.Empty,
                            [],
                            true),
                        new PlcTaskBindingItemDto(
                            "Collect",
                            "采集任务",
                            true,
                            false,
                            false,
                            [new TaskRequiredSignal("CollectDone", "Read")],
                            true,
                            string.Empty,
                            [],
                            true)
                    ])
            ]);

        public Task<IReadOnlySet<string>> GetEnabledTaskKeysAsync(
            int networkDeviceId,
            IReadOnlyCollection<TaskCandidate> candidates,
            IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
            string? deviceModel = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(
                candidates.Select(static candidate => candidate.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));

        public PlcTaskBindingValidationResult ValidateEnabledTasks(
            IReadOnlyCollection<TaskCandidate> candidates,
            IReadOnlySet<string> enabledTaskKeys,
            IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
            string? deviceModel = null)
            => PlcTaskBindingValidationResult.Success();

        public Task SaveDeviceBindingsAsync(
            int networkDeviceId,
            string moduleId,
            IReadOnlyDictionary<string, bool> taskStates,
            CancellationToken cancellationToken = default)
        {
            LastSavedNetworkDeviceId = networkDeviceId;
            LastSavedModuleId = moduleId;
            LastSavedTaskStates = taskStates.ToDictionary(static item => item.Key, static item => item.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClientPermissionService : IClientPermissionService
    {
        public bool CanEditParams { get; init; }

        public bool CanEditHardware { get; init; }

        public bool IsLocalAdmin { get; init; }

        public event Action? PermissionStateChanged;

        public bool HasPermission(string permission) => CanEditHardware || CanEditParams || IsLocalAdmin;

        public void RaiseChanged() => PermissionStateChanged?.Invoke();
    }

    private sealed class FakeStartupDiagnosticsStore(StartupDiagnosticsReport report) : IStartupDiagnosticsStore
    {
        public StartupDiagnosticsReport Current { get; private set; } = report;

        public void Update(StartupDiagnosticsReport report) => Current = report;
    }

    private sealed class FakeEdgeSyncDiagnosticsQuery : IEdgeSyncDiagnosticsQuery
    {
        public Task<EdgeSyncDiagnosticsSnapshot> GetCurrentAsync(CancellationToken ct = default)
            => Task.FromResult(new EdgeSyncDiagnosticsSnapshot(
                "PLC-A",
                CreateCloudSnapshot(),
                CreateMesSnapshot(),
                new ProductionContextPersistenceDiagnostics(1, new DateTime(2026, 5, 13, 9, 0, 0))));
    }

    private sealed class FakeStationRuntimeRegistry : IStationRuntimeRegistry
    {
        private readonly IReadOnlyDictionary<string, IStationRuntimeFactory> _registrations =
            new Dictionary<string, IStationRuntimeFactory>(StringComparer.OrdinalIgnoreCase)
            {
                ["Homogenization"] = new FakeStationRuntimeFactory()
            };

        public void Register(IStationRuntimeFactory factory)
        {
        }

        public bool HasFactory(string moduleId) => _registrations.ContainsKey(moduleId);

        public bool TryGetFactory(string moduleId, out IStationRuntimeFactory factory)
            => _registrations.TryGetValue(moduleId, out factory!);

        public IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations() => _registrations;
    }

    private sealed class FakeStationRuntimeFactory : IStationRuntimeFactory
    {
        public string ModuleId => "Homogenization";

        public IReadOnlyCollection<TaskCandidate> GetTaskCandidates()
            => [new TaskCandidate("Heartbeat", "云端心跳", [new TaskRequiredSignal("CloudHeartbeat", "Write")], true)];

        public List<IPlcTask> CreateTasks(
            IServiceProvider serviceProvider,
            IPlcBuffer buffer,
            ProductionContext context,
            IReadOnlySet<string> enabledTaskKeys)
            => [];
    }

    private sealed class FakeAvaloniaTimerFactory : IAvaloniaTimerFactory
    {
        public FakeAvaloniaTimer? LastTimer { get; private set; }

        public IAvaloniaTimer Create(TimeSpan interval)
        {
            LastTimer = new FakeAvaloniaTimer { Interval = interval };
            return LastTimer;
        }
    }

    private sealed class FakeAvaloniaTimer : IAvaloniaTimer
    {
        public event EventHandler? Tick;

        public TimeSpan Interval { get; set; }

        public bool IsEnabled { get; private set; }

        public void Start() => IsEnabled = true;

        public void Stop() => IsEnabled = false;

        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeAvaloniaDialogService : IAvaloniaDialogService
    {
        public event EventHandler<AvaloniaDialogRequest>? DialogRequested;

        public List<AvaloniaDialogRequest> Requests { get; } = [];

        public Task ShowInfoAsync(string title, string message)
        {
            var request = AvaloniaDialogRequest.CreateInfo(title, message);
            Requests.Add(request);
            DialogRequested?.Invoke(this, request);
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message)
        {
            var request = AvaloniaDialogRequest.CreateConfirm(title, message);
            Requests.Add(request);
            DialogRequested?.Invoke(this, request);
            request.Complete(true);
            return request.Result;
        }
    }

    private sealed class ImmediateAvaloniaDispatcherService : IAvaloniaDispatcherService
    {
        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }
}
