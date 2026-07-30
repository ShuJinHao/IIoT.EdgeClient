using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Time;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Identity;
using IIoT.Edge.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
namespace IIoT.Edge.Application.Tests;

public sealed class MonitorQueriesBehaviorTests
{
    private static readonly IReadOnlyList<TaskCandidate> DefaultTaskCandidates =
    [
        new("TestModule.Heartbeat", "心跳", [], IsHeartbeatLike: true),
        new("TestModule.Inbound", "扫码进站", []),
        new("TestModule.Realtime", "实时数据上传", [])
    ];

    [Fact]
    public async Task Handle_WhenProductionContextHasRuntimeState_ShouldExposeMonitorProjectionRows()
    {
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            true,
            out var contextStore,
            [
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = 7,
                    DeviceName = "Monitor-Test",
                    IsConnected = true,
                    LastConnectedAtUtc = connectedAt
                }
            ],
            [
                CreatePlcDevice(7, "Monitor-Test")
            ]);
        var context = contextStore.GetOrCreate("Monitor-Test");
        context.NetworkDeviceId = 7;
        var heartbeatAt = DateTime.UtcNow.AddSeconds(-5);

        context.SetStep("TestModule.Inbound", 10);
        context.Set("Runtime.Tasks.TestModule.Heartbeat.LastHeartbeatAtUtc", heartbeatAt);
        context.Set("Runtime.Tasks.TestModule.Realtime.LastCaptureAtUtc", DateTime.UtcNow);
        context.Set("Runtime.Tasks.TestModule.Heartbeat.LastHeartbeatIn", 1);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(7, snapshot.NetworkDeviceId);
        Assert.Equal("MONITOR-TEST", snapshot.PlcCode);
        Assert.Equal("Monitor-Test", snapshot.DeviceName);
        Assert.Equal(MonitorSnapshotSource.ProductionContext, snapshot.Source);
        Assert.True(snapshot.HasPlcConfiguration);
        Assert.True(snapshot.IsPlcConfigurationEnabled);
        Assert.Equal("127.0.0.1:6000", snapshot.PlcEndpointText);
        Assert.Contains(snapshot.StepRows, row =>
            row.Name == "TestModule.Inbound" && row.Value == "10");
        Assert.Contains(snapshot.StateMachineTaskRows, row =>
            row.Key == "TestModule.Inbound"
            && row.DisplayName == "扫码进站"
            && row.StepValue == 10
            && row.StepText == "处理中");
        Assert.Contains(snapshot.DeviceDataRows, row =>
            row.Name == "Runtime.Tasks.TestModule.Heartbeat.LastHeartbeatIn" && row.Value == "1");
        Assert.True(snapshot.IsConnected);
        Assert.NotEqual("--", snapshot.LastConnectedAtText);
        Assert.NotEqual("--", snapshot.LastHeartbeatText);
        Assert.NotEqual("--", snapshot.LastUpdatedText);
        Assert.Empty(snapshot.CellDebugRows);
    }

    [Fact]
    public async Task Handle_WhenPluginTaskCandidatesExist_ShouldUseRuntimeFactoryDisplayNames()
    {
        var candidate = new TaskCandidate("Injected.Scan", "插件扫码任务", []);
        var device = CreatePlcDevice(21, "PLC-Injected");
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out var contextStore,
            runtimeSnapshots: [],
            configuredDevices: [device],
            runtimeRegistry: new FakeStationRuntimeRegistry(new FakeStationRuntimeFactory("Injected", [candidate])),
            taskBindingService: FakePlcTaskBindingService.FromBindings(
                "Injected",
                [
                    CreateTaskBindingDevice(device, "Injected", [
                        CreateTaskBindingItem(candidate)
                    ])
                ]));
        var context = contextStore.GetOrCreate("PLC-Injected");
        context.NetworkDeviceId = 21;
        context.SetStep("Injected.Scan", 10);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        var task = Assert.Single(snapshot.StateMachineTaskRows);
        Assert.Equal("Injected.Scan", task.Key);
        Assert.Equal("插件扫码任务", task.DisplayName);
        Assert.Equal(10, task.StepValue);
        Assert.Equal("处理中", task.StepText);
        Assert.True(task.Enabled);
        Assert.True(task.CanRun);
    }

    [Fact]
    public async Task Handle_WhenStepStatesHaveKnownValues_ShouldExplainGenericStateMachineSteps()
    {
        var candidates = new[]
        {
            new TaskCandidate("Injected.Wait", "等待任务", []),
            new TaskCandidate("Injected.Processing", "处理任务", []),
            new TaskCandidate("Injected.Reset", "复位任务", []),
            new TaskCandidate("Injected.Other", "其他任务", [])
        };
        var device = CreatePlcDevice(22, "PLC-Injected");
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out var contextStore,
            runtimeSnapshots: [],
            configuredDevices: [device],
            runtimeRegistry: new FakeStationRuntimeRegistry(new FakeStationRuntimeFactory("Injected", candidates)),
            taskBindingService: FakePlcTaskBindingService.FromBindings(
                "Injected",
                [
                    CreateTaskBindingDevice(device, "Injected", candidates.Select(static candidate => CreateTaskBindingItem(candidate)).ToArray())
                ]));
        var context = contextStore.GetOrCreate("PLC-Injected");
        context.NetworkDeviceId = 22;
        context.SetStep("Injected.Wait", 0);
        context.SetStep("Injected.Processing", 10);
        context.SetStep("Injected.Reset", 30);
        context.SetStep("Injected.Other", 99);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var rows = Assert.Single(snapshots).StateMachineTaskRows.ToDictionary(row => row.Key);
        Assert.Equal("等待触发", rows["Injected.Wait"].StepText);
        Assert.Equal("处理中", rows["Injected.Processing"].StepText);
        Assert.Equal("等待 PLC 复位", rows["Injected.Reset"].StepText);
        Assert.Equal("步骤 99", rows["Injected.Other"].StepText);
    }

    [Fact]
    public async Task Handle_WhenTaskBindingIsDisabled_ShouldExposeTaskAsDisabledState()
    {
        var enabled = new TaskCandidate("Injected.Enabled", "启用任务", []);
        var disabled = new TaskCandidate("Injected.Disabled", "禁用任务", []);
        var device = CreatePlcDevice(23, "PLC-Injected");
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out _,
            runtimeSnapshots: [],
            configuredDevices: [device],
            runtimeRegistry: new FakeStationRuntimeRegistry(new FakeStationRuntimeFactory("Injected", [enabled, disabled])),
            taskBindingService: FakePlcTaskBindingService.FromBindings(
                "Injected",
                [
                    CreateTaskBindingDevice(device, "Injected", [
                        CreateTaskBindingItem(enabled),
                        CreateTaskBindingItem(disabled, enabled: false)
                    ])
                ]));

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var rows = Assert.Single(snapshots).StateMachineTaskRows.ToDictionary(row => row.Key);
        Assert.True(rows["Injected.Enabled"].Enabled);
        Assert.False(rows["Injected.Disabled"].Enabled);
        Assert.Equal("禁用任务", rows["Injected.Disabled"].DisplayName);
    }

    [Fact]
    public async Task Handle_WhenPluginTasksHaveNoSavedBindings_ShouldExposeAllDefinitionsAsDisabled()
    {
        var candidates = new[]
        {
            new TaskCandidate("Injected.Task1", "任务 1", []),
            new TaskCandidate("Injected.Task2", "任务 2", []),
            new TaskCandidate("Injected.Task3", "任务 3", []),
            new TaskCandidate("Injected.Task4", "任务 4", []),
            new TaskCandidate("Injected.Task5", "任务 5", []),
            new TaskCandidate("Injected.Task6", "任务 6", [])
        };
        var device = CreatePlcDevice(25, "PLC-Injected");
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out _,
            runtimeSnapshots: [],
            configuredDevices: [device],
            runtimeRegistry: new FakeStationRuntimeRegistry(new FakeStationRuntimeFactory("Injected", candidates)),
            taskBindingService: FakePlcTaskBindingService.FromBindings(
                "Injected",
                [
                    CreateTaskBindingDevice(
                        device,
                        "Injected",
                        candidates.Select(static candidate => CreateTaskBindingItem(
                            candidate,
                            enabled: false,
                            hasSavedBinding: false)).ToArray())
                ]));

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var rows = Assert.Single(snapshots).StateMachineTaskRows;
        Assert.Equal(6, rows.Count);
        Assert.Equal(candidates.Select(static candidate => candidate.Key), rows.Select(static row => row.Key));
        Assert.All(rows, row => Assert.False(row.Enabled));
        Assert.All(rows, row => Assert.False(row.HasSavedBinding));
    }

    [Fact]
    public async Task Handle_WhenTaskCannotRun_ShouldExposeUnavailableReasonAndMissingIoSummary()
    {
        var requiredSignal = new TaskRequiredSignal("Injected.SignalA", "Read");
        var candidate = new TaskCandidate("Injected.Blocked", "缺 IO 任务", [requiredSignal]);
        var device = CreatePlcDevice(26, "PLC-Injected");
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out _,
            runtimeSnapshots: [],
            configuredDevices: [device],
            runtimeRegistry: new FakeStationRuntimeRegistry(new FakeStationRuntimeFactory("Injected", [candidate])),
            taskBindingService: FakePlcTaskBindingService.FromBindings(
                "Injected",
                [
                    CreateTaskBindingDevice(device, "Injected", [
                        CreateTaskBindingItem(
                            candidate,
                            canRun: false,
                            unavailableReason: "缺少 IO：Injected.SignalA/Read",
                            missingRequiredSignals: [requiredSignal])
                    ])
                ]));

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var row = Assert.Single(Assert.Single(snapshots).StateMachineTaskRows);
        Assert.False(row.CanRun);
        Assert.Equal("缺少 IO：Injected.SignalA/Read", row.UnavailableReason);
        Assert.Equal(1, row.RequiredSignalCount);
        Assert.Equal(1, row.MissingRequiredSignalCount);
        Assert.Equal("Injected.SignalA/Read", row.MissingRequiredSignalsSummary);
    }

    [Fact]
    public async Task Handle_WhenRuntimeFactoryIsMissing_ShouldReturnEmptyStateMachineRows()
    {
        var candidate = new TaskCandidate("Missing.Enabled", "不应显示", []);
        var device = CreatePlcDevice(24, "PLC-Missing");
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out _,
            runtimeSnapshots: [],
            configuredDevices: [device],
            runtimeRegistry: new FakeStationRuntimeRegistry(),
            taskBindingService: FakePlcTaskBindingService.FromBindings(
                "Missing",
                [
                    CreateTaskBindingDevice(device, "Missing", [
                        CreateTaskBindingItem(candidate)
                    ])
                ]));

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        Assert.Empty(Assert.Single(snapshots).StateMachineTaskRows);
    }

    [Fact]
    public async Task Handle_WhenCurrentCellsExist_ShouldExposeCellDebugRowsWithInternalKeyAndNestedSnapshotFields()
    {
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            true,
            out var contextStore);
        var context = contextStore.GetOrCreate("Monitor-Test");

        context.AddCell(
            "TestModule.Output:ITEM-001",
            new DebugCellData
            {
                Barcode = "ITEM-001",
                RuntimeStatus = "待上传",
                CompletedTime = new DateTime(2026, 5, 26, 8, 30, 1, DateTimeKind.Utc),
                Snapshot = new DebugCellSnapshot
                {
                    Recipe = new DebugRecipeSnapshot { Name = "R-120" }
                }
            });

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        var cell = Assert.Single(snapshot.CellDebugRows);
        Assert.Equal("TestModule.Output:ITEM-001", cell.InternalKey);
        Assert.Equal("ITEM-001", cell.DisplayLabel);
        Assert.Equal("Debug", cell.ProcessType);
        Assert.Equal("待上传", cell.RuntimeStatusText);
        Assert.NotEqual("--", cell.CompletedTimeText);
        Assert.Contains(cell.FieldRows, row =>
            row.Name == "InternalKey" && row.Value == "TestModule.Output:ITEM-001");
        Assert.Contains(cell.FieldRows, row =>
            row.Name == "Barcode" && row.Value == "ITEM-001");
        Assert.Contains(cell.FieldRows, row =>
            row.Name == "Snapshot.Recipe.Name" && row.Value == "R-120");
    }

    [Fact]
    public async Task Handle_WhenCurrentCellsHaveNestedPluginData_ShouldExposeRecursiveFieldRows()
    {
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            true,
            out var contextStore);
        var context = contextStore.GetOrCreate("Monitor-Test");

        context.AddCell(
            "Debug:BAR-009",
            new DebugCellData
            {
                Barcode = "BAR-009",
                RuntimeStatus = "测试中",
                Snapshot = new DebugCellSnapshot
                {
                    Recipe = new DebugRecipeSnapshot
                    {
                        Name = "R-1"
                    }
                },
                Metrics = new Dictionary<string, object?>
                {
                    ["Nested"] = new DebugMetricSnapshot { Value = 42.5 },
                    ["Text"] = "Ready"
                },
                Samples =
                [
                    new DebugSampleSnapshot
                    {
                        Name = "S-1",
                        Result = true
                    }
                ]
            });

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var cell = Assert.Single(Assert.Single(snapshots).CellDebugRows);
        Assert.Equal("Debug:BAR-009", cell.InternalKey);
        Assert.Equal("BAR-009", cell.DisplayLabel);
        Assert.Contains(cell.FieldRows, row =>
            row.Name == "Snapshot.Recipe.Name" && row.Value == "R-1");
        Assert.Contains(cell.FieldRows, row =>
            row.Name == "Metrics.Nested.Value" && row.Value == "42.500");
        Assert.Contains(cell.FieldRows, row =>
            row.Name == "Metrics.Text" && row.Value == "Ready");
        Assert.Contains(cell.FieldRows, row =>
            row.Name == "Samples[0].Name" && row.Value == "S-1");
        Assert.Contains(cell.FieldRows, row =>
            row.Name == "Samples[0].Result" && row.Value == "OK");
    }

    [Fact]
    public async Task Handle_WhenProductionContextIsEmptyButRuntimeStatusExists_ShouldReturnPlcStatusSnapshot()
    {
        var failureAt = DateTimeOffset.UtcNow.AddSeconds(-30);
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out _,
            [
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = 9,
                    DeviceName = "PLC-Test-01",
                    IsConnected = false,
                    LastFailureAtUtc = failureAt,
                    LastError = "Connection refused"
                }
            ]);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("PLC-Test-01", snapshot.DeviceName);
        Assert.Equal(MonitorSnapshotSource.RuntimeStatus, snapshot.Source);
        Assert.False(snapshot.HasPlcConfiguration);
        Assert.False(snapshot.IsPlcConfigurationEnabled);
        Assert.Equal("--", snapshot.PlcEndpointText);
        Assert.False(snapshot.IsConnected);
        Assert.NotEqual("--", snapshot.LastFailureAtText);
        Assert.Equal("Connection refused", snapshot.LastErrorText);
        Assert.Equal("--", snapshot.LastHeartbeatText);
        Assert.NotEqual("--", snapshot.LastUpdatedText);
        Assert.Empty(snapshot.StepRows);
        Assert.Empty(snapshot.DeviceDataRows);
        Assert.Empty(snapshot.EquipmentStatusRows);
        Assert.Empty(snapshot.RealtimeRows);
        Assert.Equal(0, snapshot.CellCount);
        Assert.Empty(snapshot.CellTable.Rows);
        Assert.Empty(snapshot.CellDebugRows);
    }

    [Fact]
    public async Task Handle_WhenRuntimeAndConfiguredPlcMatch_ShouldReturnOneSnapshotWithConfiguration()
    {
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out _,
            [
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = 9,
                    DeviceName = "PLC-Test-01",
                    IsConnected = true,
                    LastConnectedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10)
                }
            ],
            [
                CreatePlcDevice(9, "PLC-Test-01")
            ]);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(9, snapshot.NetworkDeviceId);
        Assert.Equal("PLC-Test-01", snapshot.DeviceName);
        Assert.Equal(MonitorSnapshotSource.RuntimeStatus, snapshot.Source);
        Assert.True(snapshot.HasPlcConfiguration);
        Assert.True(snapshot.IsPlcConfigurationEnabled);
        Assert.Equal("127.0.0.1:6000", snapshot.PlcEndpointText);
        Assert.True(snapshot.IsConnected);
    }

    [Fact]
    public async Task Handle_WhenStablePlcIsRenamedAndRecreated_ShouldUseCurrentDisplayNameWithoutDuplicatingSnapshot()
    {
        var configured = CreatePlcDevice(
            99,
            "当前现场名称",
            plcCode: "P1-AP01");
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out var contextStore,
            [
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = 99,
                    PlcCode = "P1-AP01",
                    DeviceName = "当前现场名称",
                    IsConnected = true
                }
            ],
            [configured]);
        var contextResolution = contextStore.GetOrCreate(
            new PlcIdentity("P1-AP01", 7, "历史显示名称"));
        Assert.True(contextResolution.IsSuccess);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("P1-AP01", snapshot.PlcCode);
        Assert.Equal(99, snapshot.NetworkDeviceId);
        Assert.Equal("当前现场名称", snapshot.DeviceName);
        Assert.Equal(MonitorSnapshotSource.ProductionContext, snapshot.Source);
    }

    [Fact]
    public async Task Handle_WhenProductionContextAndRuntimeAreEmptyButDisabledConfiguredPlcExists_ShouldReturnConfiguredPlcSnapshot()
    {
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            false,
            out _,
            runtimeSnapshots: [],
            configuredDevices:
            [
                CreatePlcDevice(11, "PLC-Test-01", isEnabled: false)
            ]);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(11, snapshot.NetworkDeviceId);
        Assert.Equal("PLC-Test-01", snapshot.DeviceName);
        Assert.Equal(MonitorSnapshotSource.PlcConfiguration, snapshot.Source);
        Assert.True(snapshot.HasPlcConfiguration);
        Assert.False(snapshot.IsPlcConfigurationEnabled);
        Assert.Equal("127.0.0.1:6000", snapshot.PlcEndpointText);
        Assert.False(snapshot.IsConnected);
        Assert.Equal("--", snapshot.LastConnectedAtText);
        Assert.Equal("--", snapshot.LastFailureAtText);
        Assert.Equal("--", snapshot.LastErrorText);
        Assert.Equal("--", snapshot.LastHeartbeatText);
        Assert.Equal("--", snapshot.LastUpdatedText);
        Assert.Empty(snapshot.StepRows);
        Assert.Empty(snapshot.DeviceDataRows);
        Assert.Empty(snapshot.EquipmentStatusRows);
        Assert.Empty(snapshot.RealtimeRows);
        Assert.Empty(snapshot.CellDebugRows);
    }

    [Fact]
    public async Task Handle_WhenNoProductionContextOrRuntimeStatusExists_ShouldReturnEmptySnapshots()
    {
        var handler = CreateHandler(
            new FakeDeviceService(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            includeProductionContext: false);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task Handle_WhenUploadGateIsReady_ShouldExposeStructuredReadyStatus()
    {
        var deviceService = new FakeDeviceService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudDiagnostics = new FakeCloudDiagnosticsStore();
        var mesDiagnostics = new FakeMesUploadDiagnosticsStore();
        var mesRetryDiagnostics = new FakeMesRetryDiagnosticsStore();

        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Edge-A",
            ClientCode = "LINE-01",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "device-token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        });

        cloudDiagnostics.RecordResult("TestProcess", CloudCallResult.Success());
        mesDiagnostics.RecordSuccess("TestProcess");

        var handler = CreateHandler(
            deviceService,
            cloudRetryStore,
            mesRetryStore,
            cloudDiagnostics,
            mesRetryDiagnostics,
            mesDiagnostics);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(EdgeUploadGateState.Ready, snapshot.CloudSync.GateState);
        Assert.Equal(CloudCallOutcome.Success, snapshot.CloudSync.LastOutcome);
        Assert.Equal(MesRetryRuntimeState.Idle, snapshot.MesSync.RuntimeState);
    }

    [Fact]
    public async Task Handle_WhenUploadGateIsBlocked_ShouldExposeQueueCountsAndFailureState()
    {
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudDiagnostics = new FakeCloudDiagnosticsStore();
        var mesDiagnostics = new FakeMesUploadDiagnosticsStore();
        var mesRetryDiagnostics = new FakeMesRetryDiagnosticsStore();
        var deviceService = new FakeDeviceService
        {
            CurrentDevice = new DeviceSession
            {
                DeviceId = Guid.NewGuid(),
                DeviceName = "Edge-B",
                ClientCode = "LINE-02",
                ProcessId = Guid.NewGuid(),
                UploadAccessToken = "expired-token",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
        };
        deviceService.SetUploadGate(new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = EdgeUploadBlockReason.ExpiredUploadToken,
            TokenExpiresAtUtc = deviceService.CurrentDevice.UploadAccessTokenExpiresAtUtc,
            LastBootstrapFailedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        cloudRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1,
            Channel = "Cloud",
            ProcessType = "TestProcess",
            FailedTarget = "Cloud",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });
        mesRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 2,
            Channel = "MES",
            ProcessType = "TestProcess",
            FailedTarget = "MES",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });
        cloudDiagnostics.RecordResult("TestProcess", CloudCallResult.Failure(CloudCallOutcome.SkippedUploadNotReady, "expired_upload_token"));
        cloudDiagnostics.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
        mesRetryDiagnostics.SetRuntimeState(MesRetryRuntimeState.Backoff);
        mesDiagnostics.RecordFailure("TestProcess", "mes endpoint timeout");

        var handler = CreateHandler(
            deviceService,
            cloudRetryStore,
            mesRetryStore,
            cloudDiagnostics,
            mesRetryDiagnostics,
            mesDiagnostics);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(EdgeUploadGateState.Blocked, snapshot.CloudSync.GateState);
        Assert.Equal(EdgeUploadBlockReason.ExpiredUploadToken, snapshot.CloudSync.BlockReason);
        Assert.True(snapshot.CloudSync.IsPausedWaitingForRecovery);
        Assert.Equal(1, snapshot.CloudSync.PendingRetryCount);
        Assert.Equal(MesRetryRuntimeState.Backoff, snapshot.MesSync.RuntimeState);
        Assert.Equal(1, snapshot.MesSync.PendingRetryCount);
        Assert.Equal("mes endpoint timeout", snapshot.MesSync.LastFailureReason);
    }

    private static GetMonitorSnapshotHandler CreateHandler(
        FakeDeviceService deviceService,
        FakeFailedRecordStore cloudRetryStore,
        FakeFailedRecordStore mesRetryStore,
        FakeCloudDiagnosticsStore cloudDiagnostics,
        FakeMesRetryDiagnosticsStore mesRetryDiagnostics,
        FakeMesUploadDiagnosticsStore mesDiagnostics,
        bool includeProductionContext = true)
        => CreateHandler(
            deviceService,
            cloudRetryStore,
            mesRetryStore,
            cloudDiagnostics,
            mesRetryDiagnostics,
            mesDiagnostics,
            includeProductionContext,
            out _,
            null,
            null,
            null,
            null);

    private static GetMonitorSnapshotHandler CreateHandler(
        FakeDeviceService deviceService,
        FakeFailedRecordStore cloudRetryStore,
        FakeFailedRecordStore mesRetryStore,
        FakeCloudDiagnosticsStore cloudDiagnostics,
        FakeMesRetryDiagnosticsStore mesRetryDiagnostics,
        FakeMesUploadDiagnosticsStore mesDiagnostics,
        bool includeProductionContext,
        out FakeProductionContextStore contextStore,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot>? runtimeSnapshots = null,
        IReadOnlyCollection<NetworkDeviceEntity>? configuredDevices = null,
        IStationRuntimeRegistry? runtimeRegistry = null,
        IPlcTaskBindingService? taskBindingService = null)
    {
        contextStore = new FakeProductionContextStore();
        if (includeProductionContext)
        {
            contextStore.GetOrCreate(deviceService.CurrentDevice?.DeviceName ?? "Monitor-Test");
        }

        var devices = configuredDevices ?? [];
        var effectiveRuntimeRegistry = runtimeRegistry
            ?? new FakeStationRuntimeRegistry(new FakeStationRuntimeFactory("TestModule", DefaultTaskCandidates));
        var effectiveTaskBindingService = taskBindingService
            ?? FakePlcTaskBindingService.FromDevices(devices, DefaultTaskCandidates);

        var services = new ServiceCollection();
        services.AddEdgeApplication();
        services.AddSingleton<IProductionContextStore>(contextStore);
        services.AddSingleton<IEdgeSyncDiagnosticsQuery>(new EdgeSyncDiagnosticsQuery(
                contextStore,
                deviceService,
                cloudDiagnostics,
                mesRetryDiagnostics,
                mesDiagnostics,
                cloudRetryStore,
                mesRetryStore,
                new FakeDeviceLogBufferStore(),
                new FakeCapacityBufferStore()));
        services.AddSingleton<IProductionTimeProvider>(new FakeProductionTimeProvider());
        services.AddSingleton<IPlcConnectionManager>(new FakePlcConnectionManager(runtimeSnapshots ?? []));
        services.AddSingleton(effectiveRuntimeRegistry);
        services.AddSingleton(effectiveTaskBindingService);
        services.AddSingleton<ISender>(new MonitorHardwareSender(devices));
        services.AddTransient<GetMonitorSnapshotHandler>();

        return services.BuildServiceProvider().GetRequiredService<GetMonitorSnapshotHandler>();
    }

    private static NetworkDeviceEntity CreatePlcDevice(
        int id,
        string deviceName,
        bool isEnabled = true,
        string? plcCode = null)
    {
        var device = NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, "127.0.0.1", 6000, plcCode)
            .WithId(id);
        device.UpdateDeviceModel("S7");

        device.SetEnabled(isEnabled);
        return device;
    }

    private static PlcTaskBindingDeviceDto CreateTaskBindingDevice(
        NetworkDeviceEntity device,
        string moduleId,
        IReadOnlyList<PlcTaskBindingItemDto> tasks)
        => new(device.Id, device.DeviceName, moduleId, device.IsEnabled, tasks)
        {
            PlcCode = device.PlcCode
        };

    private static PlcTaskBindingDeviceDto CreateTaskBindingDevice(
        NetworkDeviceEntity device,
        IReadOnlyList<PlcTaskBindingItemDto> tasks)
        => CreateTaskBindingDevice(device, "TestModule", tasks);

    private static PlcTaskBindingItemDto CreateTaskBindingItem(
        TaskCandidate candidate,
        bool enabled = true,
        bool canRun = true,
        bool hasSavedBinding = true,
        string unavailableReason = "",
        IReadOnlyList<TaskRequiredSignal>? missingRequiredSignals = null)
        => new(
            candidate.Key,
            candidate.DisplayName,
            enabled,
            HasSavedBinding: hasSavedBinding,
            candidate.IsHeartbeatLike,
            candidate.RequiredSignals,
            canRun,
            unavailableReason,
            MissingRequiredSignals: missingRequiredSignals ?? [],
            IsSupportedByCurrentPlc: true);

    private sealed class DebugCellData : CellDataBase
    {
        public override string ProcessType => "Debug";

        public override string DisplayLabel => Barcode;

        public string Barcode { get; init; } = string.Empty;

        public string RuntimeStatus { get; init; } = string.Empty;

        public DebugCellSnapshot? Snapshot { get; init; }

        public Dictionary<string, object?> Metrics { get; init; } = new(StringComparer.Ordinal);

        public List<DebugSampleSnapshot> Samples { get; init; } = [];
    }

    private sealed class DebugCellSnapshot
    {
        public DebugRecipeSnapshot? Recipe { get; init; }
    }

    private sealed class DebugRecipeSnapshot
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class DebugMetricSnapshot
    {
        public double Value { get; init; }
    }

    private sealed class DebugSampleSnapshot
    {
        public string Name { get; init; } = string.Empty;

        public bool Result { get; init; }
    }

    private sealed class FakeStationRuntimeFactory(
        string moduleId,
        IReadOnlyCollection<TaskCandidate> candidates) : IStationRuntimeFactory
    {
        public string ModuleId { get; } = moduleId;

        public IReadOnlyCollection<TaskCandidate> GetTaskCandidates() => candidates;

        public List<IPlcTask> CreateTasks(
            IServiceProvider serviceProvider,
            IPlcBuffer buffer,
            ProductionContext context,
            IReadOnlySet<string> enabledTaskKeys)
            => [];
    }

    private sealed class FakeStationRuntimeRegistry(params IStationRuntimeFactory[] factories) : IStationRuntimeRegistry
    {
        private readonly Dictionary<string, IStationRuntimeFactory> _factories = factories
            .ToDictionary(static factory => factory.ModuleId, StringComparer.OrdinalIgnoreCase);

        public void Register(IStationRuntimeFactory factory)
            => _factories[factory.ModuleId] = factory;

        public bool HasFactory(string moduleId)
            => _factories.ContainsKey(moduleId);

        public bool TryGetFactory(string moduleId, out IStationRuntimeFactory factory)
            => _factories.TryGetValue(moduleId, out factory!);

        public IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations()
            => _factories;
    }

    private sealed class FakePlcTaskBindingService(
        IReadOnlyDictionary<string, IReadOnlyList<PlcTaskBindingDeviceDto>> bindingsByModule) : IPlcTaskBindingService
    {
        public static FakePlcTaskBindingService FromDevices(
            IReadOnlyCollection<NetworkDeviceEntity> devices,
            IReadOnlyList<TaskCandidate> candidates)
        {
            var rowsByModule = new Dictionary<string, IReadOnlyList<PlcTaskBindingDeviceDto>>(StringComparer.OrdinalIgnoreCase)
            {
                ["TestModule"] = devices
                    .Select(device => CreateTaskBindingDevice(
                        device,
                        "TestModule",
                        candidates.Select(static candidate => CreateTaskBindingItem(candidate)).ToArray()))
                    .ToArray()
            };

            return new FakePlcTaskBindingService(rowsByModule);
        }

        public static FakePlcTaskBindingService FromBindings(
            string moduleId,
            IReadOnlyList<PlcTaskBindingDeviceDto> deviceBindings)
            => new(new Dictionary<string, IReadOnlyList<PlcTaskBindingDeviceDto>>(StringComparer.OrdinalIgnoreCase)
            {
                [moduleId] = deviceBindings
            });

        public Task<IReadOnlyList<PlcTaskBindingDeviceDto>> GetModuleDeviceBindingsAsync(
            string moduleId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(bindingsByModule.TryGetValue(moduleId, out var bindings)
                ? bindings
                : []);

        public Task<IReadOnlySet<string>> GetEnabledTaskKeysAsync(
            int networkDeviceId,
            IReadOnlyCollection<TaskCandidate> candidates,
            IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
            string? deviceModel = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlySet<string>> GetConfiguredEnabledTaskKeysAsync(
            int networkDeviceId,
            IReadOnlyCollection<TaskCandidate> candidates,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public PlcTaskBindingValidationResult ValidateEnabledTasks(
            IReadOnlyCollection<TaskCandidate> candidates,
            IReadOnlySet<string> enabledTaskKeys,
            IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
            string? deviceModel = null)
            => throw new NotSupportedException();
    }

    private sealed class MonitorHardwareSender(IReadOnlyCollection<NetworkDeviceEntity> devices) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetAllNetworkDevicesQuery)
            {
                return Task.FromResult((TResponse)(object)Result.Success(devices.ToList()));
            }

            throw new NotSupportedException(request.GetType().Name);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException(request?.GetType().Name);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().Name);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakePlcConnectionManager(IReadOnlyCollection<PlcConnectionRuntimeSnapshot> snapshots) : IPlcConnectionManager
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReloadAsync(string deviceName, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default) => Task.CompletedTask;

        public void RegisterTasks(
            string deviceName,
            Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        {
        }

        public IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        {
        }

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId)
            => snapshots.FirstOrDefault(snapshot => snapshot.NetworkDeviceId == networkDeviceId);

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses() => snapshots;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
