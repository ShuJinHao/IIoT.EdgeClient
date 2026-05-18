using System.Reflection;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using System.Data;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Diagnostics;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Presentation.Navigation.Avalonia;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.Presentation.Panels.Avalonia;
using IIoT.Edge.Presentation.Panels.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Text;
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
            new FakeEquipmentPanelService(),
            new FakeEdgeSyncDiagnosticsQuery(),
            new FakeDisplayLogService(),
            new AvaloniaRuntimeState(),
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
    public void Log_view_model_uses_file_timestamp_and_marks_unknown_time_without_now_fallback()
    {
        var runtimePaths = CreateRuntimePaths();
        Directory.CreateDirectory(runtimePaths.LogDirectory);
        File.WriteAllLines(
            Path.Combine(runtimePaths.LogDirectory, "runtime.log"),
            [
                "2026-05-15 10:21:34 [ERROR] Cloud upload failed",
                "ERROR line without timestamp"
            ],
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var viewModel = new LogViewModel(
            new FakeDisplayLogService(),
            runtimePaths,
            new FakeStartupDiagnosticsStore(StartupDiagnosticsReport.Empty()),
            new ImmediateAvaloniaDispatcherService(),
            CreatePanelLanguageService());

        var parsed = Assert.Single(
            viewModel.Entries,
            entry => entry.Message.Contains("Cloud upload failed", StringComparison.Ordinal));
        Assert.Equal(new DateTime(2026, 5, 15, 10, 21, 34), parsed.Time);
        Assert.Equal("10:21:34", parsed.TimeText);

        var unknown = Assert.Single(
            viewModel.Entries,
            entry => entry.Message.Contains("without timestamp", StringComparison.Ordinal));
        Assert.Equal(DateTime.MinValue, unknown.Time);
        Assert.Equal("未知时间", unknown.TimeText);
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

        Assert.Contains("已导出", viewModel.FeedbackMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_view_model_exports_empty_records_through_unified_export_service()
    {
        var service = new FakeDataViewService
        {
            Snapshot = new DataViewSnapshot(0, 0, 0, "0.00%", [])
        };
        var exportService = new FakeDataExportService
        {
            Result = AvaloniaDataExportResult.Success("C:\\export\\DataView.csv")
        };
        var viewModel = new DataViewModel(
            service,
            CreateLanguageService(),
            CreateRuntimePaths(),
            exportService,
            "test.data",
            "Navigation_Title_Data",
            "生产数据");

        await viewModel.OnActivatedAsync();
        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Equal(1, exportService.CallCount);
        Assert.Equal("DataView", exportService.LastRequest?.PageType);
        Assert.Empty(exportService.LastRows);
        Assert.Contains("已导出", viewModel.FeedbackMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_view_model_export_failure_shows_chinese_feedback()
    {
        var exportService = new FakeDataExportService
        {
            Result = AvaloniaDataExportResult.Failure("磁盘已满")
        };
        var viewModel = new DataViewModel(
            new FakeDataViewService(),
            CreateLanguageService(),
            CreateRuntimePaths(),
            exportService,
            "test.data",
            "Navigation_Title_Data",
            "生产数据");

        await viewModel.OnActivatedAsync();
        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Equal(1, exportService.CallCount);
        Assert.Contains("导出生产数据失败：磁盘已满", viewModel.FeedbackMessage, StringComparison.Ordinal);
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
    public async Task Capacity_view_model_export_failure_shows_chinese_feedback()
    {
        var service = new FakeCapacityViewService
        {
            IsOnline = true,
            TodayResult = CreateCapacityResult("08:00", 32)
        };
        var exportService = new FakeDataExportService
        {
            Result = AvaloniaDataExportResult.Failure("目录不可写")
        };
        var viewModel = new CapacityViewModel(
            service,
            CreateLanguageService(),
            new FakeAvaloniaDialogService(),
            new ImmediateAvaloniaDispatcherService(),
            CreateRuntimePaths(),
            exportService,
            "test.capacity",
            "Navigation_Title_CapacityQuery",
            "产能查询");

        await viewModel.OnActivatedAsync();
        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Equal(1, exportService.CallCount);
        Assert.Equal("Capacity", exportService.LastRequest?.PageType);
        Assert.Contains("导出产能数据失败：目录不可写", viewModel.FeedbackMessage, StringComparison.Ordinal);
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
            new EdgeSyncDiagnosticStatusClassifier(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();

        Assert.Single(viewModel.RuntimeRegistrations);
        Assert.Single(viewModel.ModuleRegistrations);
        Assert.Equal(3, viewModel.PersistenceRows.Count);
        Assert.Single(viewModel.PluginStates);
        Assert.Single(viewModel.Issues);
        Assert.Contains(viewModel.FieldAcceptanceRows, row => row.Scope == "Cloud 状态");
        Assert.Contains(viewModel.FieldAcceptanceRows, row => row.Scope == "MES 状态");
        Assert.Contains("运行时注册 1 个", viewModel.FeedbackMessage, StringComparison.Ordinal);
        Assert.Contains("运行目录：runtime-data", viewModel.ConfigurationProfileText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_view_model_loads_cloud_and_mes_dead_letters_with_details()
    {
        var cloudDeadLetter = CreateDeadLetter(10, "Homogenization", "failed_cloud_records");
        cloudDeadLetter.CellDataJson = "{\"barcode\":\"CLOUD-001\"}";
        var mesDeadLetter = CreateDeadLetter(20, "Homogenization", "failed_mes_records");
        mesDeadLetter.CellDataJson = "{\"barcode\":\"MES-001\"}";

        var services = new ServiceCollection();
        services.AddSingleton<IEdgeSyncDiagnosticsQuery>(new FakeEdgeSyncDiagnosticsQuery(
            CreateEdgeSyncSnapshot(
                CreateDeadLetterSnapshot(cloudDeadLetter),
                CreateDeadLetterSnapshot(mesDeadLetter))));
        var provider = services.BuildServiceProvider();

        var viewModel = new DiagnosticsViewModel(
            provider,
            CreateLanguageService(),
            new EdgeSyncDiagnosticStatusClassifier(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();

        var cloudRow = Assert.Single(viewModel.CloudDeadLetters);
        Assert.Equal(DataPipelineRetryChannel.Cloud, cloudRow.Channel);
        Assert.Equal(10, cloudRow.Id);
        Assert.Equal("Homogenization", cloudRow.ProcessType);
        Assert.Contains("CLOUD-001", cloudRow.CellDataJson, StringComparison.Ordinal);

        var mesRow = Assert.Single(viewModel.MesDeadLetters);
        Assert.Equal(DataPipelineRetryChannel.Mes, mesRow.Channel);
        Assert.Equal(20, mesRow.Id);
        Assert.Contains("MES-001", mesRow.CellDataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_dead_letter_commands_require_local_admin()
    {
        var maintenance = new FakeDeadLetterMaintenanceService();
        var services = CreateDiagnosticsDeadLetterServices(
            maintenance,
            new FakeClientPermissionService { IsLocalAdmin = false },
            new FakeAvaloniaDialogService());
        var provider = services.BuildServiceProvider();
        var viewModel = new DiagnosticsViewModel(
            provider,
            CreateLanguageService(),
            new EdgeSyncDiagnosticStatusClassifier(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();

        var row = Assert.Single(viewModel.CloudDeadLetters);
        Assert.False(viewModel.CanOperateDeadLetters);
        Assert.False(viewModel.RequeueDeadLetterCommand.CanExecute(row));
        Assert.False(viewModel.DeleteDeadLetterCommand.CanExecute(row));
        Assert.Equal(0, maintenance.RequeueCalls);
        Assert.Equal(0, maintenance.DeleteCalls);
    }

    [Fact]
    public async Task Diagnostics_dead_letter_requeue_cancel_does_not_call_maintenance_service()
    {
        var maintenance = new FakeDeadLetterMaintenanceService();
        var dialog = new FakeAvaloniaDialogService { ConfirmResult = false };
        var services = CreateDiagnosticsDeadLetterServices(
            maintenance,
            new FakeClientPermissionService { IsLocalAdmin = true },
            dialog);
        var provider = services.BuildServiceProvider();
        var viewModel = new DiagnosticsViewModel(
            provider,
            CreateLanguageService(),
            new EdgeSyncDiagnosticStatusClassifier(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();
        var row = Assert.Single(viewModel.CloudDeadLetters);
        await viewModel.RequeueDeadLetterCommand.ExecuteAsync(row);

        Assert.Equal(0, maintenance.RequeueCalls);
        Assert.Contains("已取消死信重新入队", viewModel.FeedbackMessage, StringComparison.Ordinal);
        Assert.Contains("重新写入对应 retry 队列", Assert.Single(dialog.Requests).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_dead_letter_requeue_and_delete_call_selected_channel_only()
    {
        var maintenance = new FakeDeadLetterMaintenanceService();
        var dialog = new FakeAvaloniaDialogService { ConfirmResult = true };
        var services = CreateDiagnosticsDeadLetterServices(
            maintenance,
            new FakeClientPermissionService { IsLocalAdmin = true },
            dialog);
        var provider = services.BuildServiceProvider();
        var viewModel = new DiagnosticsViewModel(
            provider,
            CreateLanguageService(),
            new EdgeSyncDiagnosticStatusClassifier(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();
        var cloudRow = Assert.Single(viewModel.CloudDeadLetters);
        var mesRow = Assert.Single(viewModel.MesDeadLetters);

        await viewModel.RequeueDeadLetterCommand.ExecuteAsync(cloudRow);
        await viewModel.DeleteDeadLetterCommand.ExecuteAsync(mesRow);

        Assert.Equal(1, maintenance.RequeueCalls);
        Assert.Equal(DataPipelineRetryChannel.Cloud, maintenance.LastRequeueChannel);
        Assert.Equal(10, maintenance.LastRequeueId);
        Assert.Equal(1, maintenance.DeleteCalls);
        Assert.Equal(DataPipelineRetryChannel.Mes, maintenance.LastDeleteChannel);
        Assert.Equal(20, maintenance.LastDeleteId);
        Assert.Contains("MES 死信已删除", viewModel.FeedbackMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_view_model_projects_io_write_gate_audit_rows_for_field_trace()
    {
        var auditStore = new IoViewWriteGateAuditStore();
        auditStore.Record(new IoViewWriteGateAuditEntry(
            new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.Zero),
            "PLC-A",
            "Start",
            IoViewWriteResultKind.RuntimeNotStarted,
            "Runtime not started; buffer write was blocked.",
            1));
        auditStore.Record(new IoViewWriteGateAuditEntry(
            new DateTimeOffset(2026, 5, 14, 9, 5, 0, TimeSpan.Zero),
            "PLC-A",
            "Start",
            IoViewWriteResultKind.AcceptedToRuntimeBuffer,
            "Accepted to runtime buffer; PLC write waits for runtime block policy.",
            12));

        var services = new ServiceCollection();
        services.AddSingleton<IIoViewWriteGateAuditStore>(auditStore);
        var provider = services.BuildServiceProvider();

        var viewModel = new DiagnosticsViewModel(
            provider,
            CreateLanguageService(),
            new EdgeSyncDiagnosticStatusClassifier(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();

        Assert.Equal(2, viewModel.IoWriteGateRows.Count);
        var latest = viewModel.IoWriteGateRows[0];
        Assert.Equal("PLC-A", latest.DeviceName);
        Assert.Equal("Start", latest.BusinessGroup);
        Assert.Equal("12", latest.Value);
        Assert.Contains("Accepted to runtime buffer", latest.Message, StringComparison.Ordinal);

        var previous = viewModel.IoWriteGateRows[1];
        Assert.Equal("1", previous.Value);
        Assert.Contains("blocked", previous.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_view_model_projects_plc_block_write_trace_rows_without_actions()
    {
        var traceStore = new FakePlcIoWriteTraceStore();
        traceStore.Record(new PlcIoWriteTraceEntry(
            new DateTimeOffset(2026, 5, 14, 10, 0, 0, TimeSpan.Zero),
            PlcIoWriteTraceKind.Attempt,
            1,
            "PLC-A",
            "D100",
            2,
            ["Start.Reply"],
            null));
        traceStore.Record(new PlcIoWriteTraceEntry(
            new DateTimeOffset(2026, 5, 14, 10, 0, 1, TimeSpan.Zero),
            PlcIoWriteTraceKind.Success,
            1,
            "PLC-A",
            "D100",
            2,
            ["Start.Reply"],
            null));

        var services = new ServiceCollection();
        services.AddSingleton<IPlcIoWriteTraceStore>(traceStore);
        var provider = services.BuildServiceProvider();

        var viewModel = new DiagnosticsViewModel(
            provider,
            CreateLanguageService(),
            new EdgeSyncDiagnosticStatusClassifier(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();

        Assert.Equal(2, viewModel.PlcWriteTraceRows.Count);
        var latest = viewModel.PlcWriteTraceRows[0];
        Assert.Equal("PLC-A", latest.DeviceName);
        Assert.Equal("成功", latest.Kind);
        Assert.Equal("D100", latest.StartAddress);
        Assert.Equal("2", latest.WordCount);
        Assert.Contains("Start.Reply", latest.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_view_model_builds_field_acceptance_summary_for_site_handoff()
    {
        var auditStore = new IoViewWriteGateAuditStore();
        auditStore.Record(new IoViewWriteGateAuditEntry(
            new DateTimeOffset(2026, 5, 14, 11, 0, 0, TimeSpan.Zero),
            "PLC-A",
            "Start",
            IoViewWriteResultKind.AcceptedToRuntimeBuffer,
            "已进入运行时缓冲，等待扫描任务按块写入。",
            12));

        var traceStore = new FakePlcIoWriteTraceStore();
        traceStore.Record(new PlcIoWriteTraceEntry(
            new DateTimeOffset(2026, 5, 14, 11, 0, 2, TimeSpan.Zero),
            PlcIoWriteTraceKind.Success,
            1,
            "PLC-A",
            "D100",
            2,
            ["Start.Reply"],
            null));

        var runtimeState = new AvaloniaRuntimeState();
        runtimeState.SetStatus(
            AvaloniaRuntimeStatus.Running,
            "运行链路已启动，允许现场只读联调和受控缓冲写入。",
            "模块数：1；PLC 设备数：1",
            "C:\\runtime\\diagnostics\\logs");

        var services = new ServiceCollection();
        services.AddSingleton<IAvaloniaRuntimeState>(runtimeState);
        services.AddSingleton<IStartupDiagnosticsStore>(new FakeStartupDiagnosticsStore(CreateStartupDiagnosticsReport()));
        services.AddSingleton<IEdgeSyncDiagnosticsQuery>(new FakeEdgeSyncDiagnosticsQuery());
        services.AddSingleton<IIoViewWriteGateAuditStore>(auditStore);
        services.AddSingleton<IPlcIoWriteTraceStore>(traceStore);
        var provider = services.BuildServiceProvider();

        var viewModel = new DiagnosticsViewModel(
            provider,
            CreateLanguageService(),
            new EdgeSyncDiagnosticStatusClassifier(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();

        Assert.Equal(9, viewModel.FieldAcceptanceRows.Count);
        Assert.Contains(viewModel.FieldAcceptanceRows, row =>
            row.Scope == "运行模式" &&
            row.Status == "--start-runtime" &&
            row.Message.Contains("允许现场只读联调", StringComparison.Ordinal));
        Assert.Contains(viewModel.FieldAcceptanceRows, row =>
            row.Scope == "I/O 写入申请" &&
            row.Message.Contains("等待扫描任务按块写入", StringComparison.Ordinal));
        Assert.Contains(viewModel.FieldAcceptanceRows, row =>
            row.Scope == "PLC 块写入轨迹" &&
            row.Status == "成功" &&
            row.Message.Contains("Start.Reply", StringComparison.Ordinal));
        Assert.Contains(viewModel.FieldAcceptanceRows, row => row.Scope == "Cloud 状态" && row.Status == "已就绪");
        Assert.Contains(viewModel.FieldAcceptanceRows, row => row.Scope == "MES 状态" && row.Status == "空闲");
        Assert.Contains(viewModel.FieldAcceptanceRows, row =>
            row.Scope == "运行目录证据" &&
            row.Message.Contains("C:\\runtime\\diagnostics\\logs", StringComparison.Ordinal));
        Assert.Contains(viewModel.FieldAcceptanceRows, row =>
            row.Scope == "Cloud/MES 差异" &&
            row.Message.Contains("不合并补偿链路", StringComparison.Ordinal));
        Assert.Contains(viewModel.FieldAcceptanceRows, row =>
            row.Scope == "Cloud/MES 死信运维" &&
            row.Status == "只读" &&
            row.Message.Contains("重新入队或删除本地死信记录", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Diagnostics_view_model_marks_field_acceptance_summary_as_ui_only_without_runtime_argument()
    {
        var runtimeState = new AvaloniaRuntimeState();
        var services = new ServiceCollection();
        services.AddSingleton<IAvaloniaRuntimeState>(runtimeState);
        var provider = services.BuildServiceProvider();

        var viewModel = new DiagnosticsViewModel(
            provider,
            CreateLanguageService(),
            new EdgeSyncDiagnosticStatusClassifier(),
            "Core.Diagnostics",
            "Navigation_Menu_CoreDiagnostics",
            "系统诊断");

        await viewModel.OnActivatedAsync();

        var runtimeRow = Assert.Single(viewModel.FieldAcceptanceRows, row => row.Scope == "运行模式");
        Assert.Equal("UI-only", runtimeRow.Status);
        Assert.Contains("--start-runtime", runtimeRow.Message, StringComparison.Ordinal);
        Assert.Contains("运行链路未启动", runtimeRow.Message, StringComparison.Ordinal);
    }

    private static IAvaloniaLanguageService CreateLanguageService()
    {
        var service = new AvaloniaResourceLanguageService(
            new AvaloniaXamlStringResourceLoader().Load([typeof(NavigationAvaloniaPresentationRegistration).Assembly]));
        service.Apply("zh-CN");
        return service;
    }

    private static IAvaloniaLanguageService CreatePanelLanguageService()
    {
        var service = new AvaloniaResourceLanguageService(
            new AvaloniaXamlStringResourceLoader().Load([typeof(PanelAvaloniaPresentationRegistration).Assembly]));
        service.Apply("zh-CN");
        return service;
    }

    private static EdgeRuntimePaths CreateRuntimePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "iiot-edge-avalonia-tests", Guid.NewGuid().ToString("N"));
        return new EdgeRuntimePaths(
            root,
            "tests",
            root,
            Path.Combine(root, "db"),
            Path.Combine(root, "context"),
            Path.Combine(root, "recipes"),
            Path.Combine(root, "excel"),
            Path.Combine(root, "diagnostics"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "diagnostics", "device-cache.json"),
            Path.Combine(root, "logs", "crash.log"),
            Path.Combine(root, "logs", "crash-fallback.log"));
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

    private static CloudSyncDiagnosticsSnapshot CreateCloudSnapshot(DeadLetterDiagnosticsSnapshot? deadLetters = null)
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
            DeadLetters: deadLetters,
            PendingPassStationCount: 3);

    private static MesSyncDiagnosticsSnapshot CreateMesSnapshot(DeadLetterDiagnosticsSnapshot? deadLetters = null)
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
            PersistenceFaultMessage: null,
            DeadLetters: deadLetters);

    private static EdgeSyncDiagnosticsSnapshot CreateEdgeSyncSnapshot(
        DeadLetterDiagnosticsSnapshot? cloudDeadLetters = null,
        DeadLetterDiagnosticsSnapshot? mesDeadLetters = null)
        => new(
            "PLC-A",
            CreateCloudSnapshot(cloudDeadLetters),
            CreateMesSnapshot(mesDeadLetters),
            new ProductionContextPersistenceDiagnostics(1, new DateTime(2026, 5, 13, 9, 0, 0)));

    private static DeadLetterDiagnosticsSnapshot CreateDeadLetterSnapshot(params DeadLetterRecord[] records)
        => new(records.Length, [], records, false, null, null);

    private static DeadLetterRecord CreateDeadLetter(long id, string processType, string sourceTable)
        => new()
        {
            Id = id,
            ProcessType = processType,
            CellDataJson = "{\"barcode\":\"CELL-001\"}",
            FailedTarget = sourceTable.Contains("mes", StringComparison.OrdinalIgnoreCase) ? "MES" : "Cloud",
            SourceTable = sourceTable,
            SourceRecordId = id + 1000,
            FailureStage = "FallbackPersist",
            FailureReason = "测试失败原因",
            CreatedAt = new DateTime(2026, 5, 13, 10, 0, 0)
        };

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

    private static ServiceCollection CreateDiagnosticsDeadLetterServices(
        FakeDeadLetterMaintenanceService maintenanceService,
        FakeClientPermissionService permissionService,
        FakeAvaloniaDialogService dialogService)
    {
        var cloudDeadLetter = CreateDeadLetter(10, "Homogenization", "failed_cloud_records");
        var mesDeadLetter = CreateDeadLetter(20, "Homogenization", "failed_mes_records");
        var services = new ServiceCollection();
        services.AddSingleton(CreateLanguageService());
        services.AddSingleton<IAvaloniaDialogService>(dialogService);
        services.AddSingleton<IClientPermissionService>(permissionService);
        services.AddSingleton<IDeadLetterMaintenanceService>(maintenanceService);
        services.AddSingleton<IEdgeSyncDiagnosticsQuery>(new FakeEdgeSyncDiagnosticsQuery(
            CreateEdgeSyncSnapshot(
                CreateDeadLetterSnapshot(cloudDeadLetter),
                CreateDeadLetterSnapshot(mesDeadLetter))));
        services.AddNavigationAvaloniaPresentation();
        return services;
    }

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

    private sealed class FakeEquipmentPanelService : IEquipmentPanelService
    {
        public List<HardwareSnapshot> Hardware { get; init; } = [];

        public Task<List<HardwareSnapshot>> GetHardwareStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Hardware);

        public Task<RecipeSnapshot?> GetRecipeSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<RecipeSnapshot?>(null);

        public Task<CapacitySnapshot> GetCapacitySnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CapacitySnapshot(0, 0, "0.0%", "--"));
    }

    private sealed class FakeDisplayLogService : ILogDisplayService
    {
        public event Action<LogEntry>? EntryAdded;

        public ObservableCollection<LogEntry> Entries { get; } = [];

        public void Debug(string message) => Add("DEBUG", message);

        public void Info(string message) => Add("INFO", message);

        public void Warn(string message) => Add("WARN", message);

        public void Error(string message) => Add("ERROR", message);

        public void Fatal(string message) => Add("FATAL", message);

        private void Add(string level, string message)
        {
            var entry = new LogEntry { Time = DateTime.Now, Level = level, Message = message };
            Entries.Add(entry);
            EntryAdded?.Invoke(entry);
        }
    }

    private sealed class FakeDeadLetterMaintenanceService : IDeadLetterMaintenanceService
    {
        public int RequeueCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public DataPipelineRetryChannel? LastRequeueChannel { get; private set; }

        public long? LastRequeueId { get; private set; }

        public DataPipelineRetryChannel? LastDeleteChannel { get; private set; }

        public long? LastDeleteId { get; private set; }

        public Task<IReadOnlyList<DeadLetterRecord>> GetLatestAsync(
            DataPipelineRetryChannel channel,
            int count = 50)
            => Task.FromResult<IReadOnlyList<DeadLetterRecord>>([]);

        public Task<DeadLetterRecord?> GetByIdAsync(DataPipelineRetryChannel channel, long id)
            => Task.FromResult<DeadLetterRecord?>(CreateDeadLetter(id, "Homogenization", channel == DataPipelineRetryChannel.Mes ? "failed_mes_records" : "failed_cloud_records"));

        public Task<DeadLetterOperationResult> RequeueAsync(DataPipelineRetryChannel channel, long id)
        {
            RequeueCalls++;
            LastRequeueChannel = channel;
            LastRequeueId = id;
            return Task.FromResult(DeadLetterOperationResult.Success($"{FormatChannel(channel)}死信已重新写入 retry 队列。"));
        }

        public Task<DeadLetterOperationResult> DeleteAsync(DataPipelineRetryChannel channel, long id)
        {
            DeleteCalls++;
            LastDeleteChannel = channel;
            LastDeleteId = id;
            return Task.FromResult(DeadLetterOperationResult.Success($"{FormatChannel(channel)} 死信已删除。"));
        }

        private static string FormatChannel(DataPipelineRetryChannel channel)
            => channel == DataPipelineRetryChannel.Mes ? "MES" : "云端";
    }

    private sealed class FakeDataViewService : IDataViewService
    {
        public DateTime LastDateFrom { get; private set; }

        public DateTime LastDateTo { get; private set; }

        public DataViewSnapshot Snapshot { get; init; } = new(
            128,
            126,
            2,
            "98.44%",
            [new ProductionRecordItem("08:00", "B-001", 64, 63, 1, "98.44%")]);

        public Task<DataViewSnapshot> QueryAsync(
            DateTime dateFrom,
            DateTime dateTo,
            CancellationToken cancellationToken = default)
        {
            LastDateFrom = dateFrom;
            LastDateTo = dateTo;
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class FakeDataExportService : IAvaloniaDataExportService
    {
        public AvaloniaDataExportResult Result { get; init; } = AvaloniaDataExportResult.Success("C:\\export\\data.csv");

        public int CallCount { get; private set; }

        public AvaloniaDataExportRequest? LastRequest { get; private set; }

        public IReadOnlyList<IReadOnlyList<object?>> LastRows { get; private set; } = [];

        public Task<AvaloniaDataExportResult> ExportAsync(
            AvaloniaDataExportRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            LastRows = request.Rows.ToArray();
            return Task.FromResult(Result);
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
        private readonly EdgeSyncDiagnosticsSnapshot _snapshot;

        public FakeEdgeSyncDiagnosticsQuery()
            : this(CreateEdgeSyncSnapshot())
        {
        }

        public FakeEdgeSyncDiagnosticsQuery(EdgeSyncDiagnosticsSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<EdgeSyncDiagnosticsSnapshot> GetCurrentAsync(CancellationToken ct = default)
            => Task.FromResult(_snapshot);
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

    private sealed class FakePlcIoWriteTraceStore : IPlcIoWriteTraceStore
    {
        private readonly List<PlcIoWriteTraceEntry> _entries = [];

        public void Record(PlcIoWriteTraceEntry entry)
            => _entries.Insert(0, entry);

        public IReadOnlyList<PlcIoWriteTraceEntry> GetRecent(int count = 50)
            => _entries.Take(Math.Max(1, count)).ToArray();

        public PlcIoWriteTraceEntry? GetLatestForSignals(int deviceId, IReadOnlyCollection<string> signalKeys)
        {
            var keys = signalKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _entries.FirstOrDefault(entry =>
                entry.DeviceId == deviceId &&
                entry.SignalKeys.Any(keys.Contains));
        }
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

        public bool ConfirmResult { get; init; } = true;

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
            request.Complete(ConfirmResult);
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
