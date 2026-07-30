using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class MonitorViewModelBehaviorTests
{
    [Fact]
    public async Task OnActivatedAsync_WhenSharedSelectionIsAll_ShouldNotAutoSelectFirstSnapshot()
    {
        var selectionService = new DeviceSelectionService();
        var viewModel = CreateViewModel(
            selectionService,
            [
                CreateSnapshot(1, "PLC-A01"),
                CreateSnapshot(2, "PLC-A02")
            ]);

        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal(["PLC-A01", "PLC-A02"], viewModel.DeviceOptions.ToArray());
        Assert.Null(viewModel.SelectedDevice);
        Assert.Empty(viewModel.DeviceDataRows);
        Assert.Empty(viewModel.StateMachineTaskItems);
        Assert.Equal(IDeviceSelectionService.AllFilterKey, selectionService.SelectedDeviceKey);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenSharedSelectionMatchesDevice_ShouldShowSelectedDeviceSnapshot()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A02");
        var viewModel = CreateViewModel(
            selectionService,
            [
                CreateSnapshot(1, "PLC-A01", deviceDataValue: "A"),
                CreateSnapshot(2, "PLC-A02", deviceDataValue: "B")
            ]);

        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal("PLC-A02", viewModel.SelectedDevice);
        Assert.Equal("CODE-2 · PLC-A02", viewModel.SelectedDeviceDisplayName);
        Assert.Equal("B", Assert.Single(viewModel.DeviceDataRows).Value);
        Assert.Equal("PLC-A02", Assert.Single(viewModel.CellDebugItems).DeviceName);
        Assert.Equal("上传任务", Assert.Single(viewModel.StateMachineTaskItems).DisplayName);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenSharedSelectionUsesRealName_ShouldRetainStablePlcIdentity()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("改名后的显示名", "CODE-2")
        ]);
        selectionService.SelectDevice("改名后的显示名");
        var viewModel = CreateViewModel(
            selectionService,
            [
                CreateSnapshot(1, "旧显示名"),
                CreateSnapshot(2, "改名后的显示名", deviceDataValue: "stable")
            ]);

        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal("改名后的显示名", viewModel.SelectedDevice);
        Assert.Equal("CODE-2 · 改名后的显示名", viewModel.SelectedDeviceDisplayName);
        Assert.Equal("stable", Assert.Single(viewModel.DeviceDataRows).Value);
        Assert.Equal("改名后的显示名", selectionService.SelectedDeviceKey);
        Assert.Equal("CODE-2", selectionService.SelectedPlcCode);
    }

    [Fact]
    public async Task SelectedDevice_WhenSetInsideMonitorPage_ShouldNotWriteSharedSelection()
    {
        var selectionService = new DeviceSelectionService();
        var viewModel = CreateViewModel(
            selectionService,
            [
                CreateSnapshot(1, "PLC-A01"),
                CreateSnapshot(2, "PLC-A02")
            ]);
        await viewModel.OnActivatedAsync();

        viewModel.SelectedDevice = "PLC-A01";
        await viewModel.OnDeactivatedAsync();

        Assert.Equal(IDeviceSelectionService.AllFilterKey, selectionService.SelectedDeviceKey);
        Assert.Equal("PLC-A01", viewModel.SelectedDevice);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenSharedSelectionHasNoSnapshot_ShouldKeepSelectedDeviceAndShowEmpty()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A99");
        var viewModel = CreateViewModel(
            selectionService,
            [
                CreateSnapshot(1, "PLC-A01")
            ]);

        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal("PLC-A99", viewModel.SelectedDevice);
        Assert.Empty(viewModel.DeviceDataRows);
        Assert.Equal("PLC-A99", selectionService.SelectedDeviceKey);
    }

    private static MonitorViewModel CreateViewModel(
        IDeviceSelectionService selectionService,
        IReadOnlyList<DeviceMonitorSnapshot> snapshots)
    {
        var languageService = new TestAppLanguageService();
        var collaboratorFactory = new MonitorViewModelCollaboratorFactory(
            languageService,
            new MonitorViewModelSummaryFormatter(languageService),
            new MonitorStateMachineTaskItemFactory(languageService));

        return new MonitorViewModel(
            new FakeMonitorSnapshotQueryFacade(snapshots),
            languageService,
            collaboratorFactory,
            selectionService);
    }

    private static DeviceMonitorSnapshot CreateSnapshot(
        int deviceId,
        string deviceName,
        string deviceDataValue = "value")
        => new(
            deviceId,
            deviceName,
            MonitorSnapshotSource.PlcConfiguration,
            HasPlcConfiguration: true,
            IsPlcConfigurationEnabled: true,
            PlcEndpointText: "127.0.0.1:102",
            StepRows: [new MonitorSnapshotRow(deviceName, "步骤", "等待触发")],
            StateMachineTaskRows:
            [
                new MonitorStateMachineTaskSnapshot(
                    "Task.Upload",
                    "上传任务",
                    Enabled: true,
                    CanRun: true,
                    HasSavedBinding: true,
                    StepValue: 0,
                    StepText: "等待触发",
                    UnavailableReason: string.Empty,
                    IsHeartbeatLike: false,
                    RequiredSignalCount: 1,
                    MissingRequiredSignalCount: 0,
                    MissingRequiredSignalsSummary: string.Empty)
            ],
            DeviceDataRows: [new MonitorSnapshotRow(deviceName, "设备数据", deviceDataValue)],
            EquipmentStatusRows: [new MonitorSnapshotRow(deviceName, "设备状态", "运行")],
            RealtimeRows: [new MonitorSnapshotRow(deviceName, "实时数据", "10")],
            IsConnected: false,
            ConnectionState: PlcConnectionState.Disconnected,
            LastConnectedAtText: "--",
            LastFailureAtText: "--",
            LastErrorText: "--",
            LastHeartbeatText: "--",
            LastUpdatedText: "--",
            CellCount: 1,
            CellTable: new MonitorCellTableSnapshot(["设备"], [new MonitorCellTableRow([deviceName])]),
            CellDebugRows:
            [
                new MonitorCellDebugSnapshot(
                    deviceName,
                    $"{deviceName}-CELL",
                    $"{deviceName}-CELL",
                    "TestPlugin",
                    "运行",
                    "--",
                    [new MonitorSnapshotRow(deviceName, "字段", "值")])
            ],
            CloudSync: CreateCloudSync(),
            MesSync: CreateMesSync(),
            ContextPersistence: new ProductionContextPersistenceDiagnostics(0, null))
        {
            PlcCode = $"CODE-{deviceId}"
        };

    private static CloudSyncDiagnosticsSnapshot CreateCloudSync()
        => new(
            EdgeUploadGateState.Ready,
            EdgeUploadBlockReason.None,
            CloudRetryRuntimeState.Idle,
            null,
            null,
            null,
            CloudCallOutcome.Success,
            "none",
            null,
            0,
            0,
            0,
            false,
            false,
            null,
            "none",
            null,
            false,
            null,
            null);

    private static MesSyncDiagnosticsSnapshot CreateMesSync()
        => new(
            MesRetryRuntimeState.Idle,
            null,
            null,
            null,
            null,
            0,
            [],
            false,
            null,
            "none",
            null,
            false,
            null,
            null);

    private sealed class FakeMonitorSnapshotQueryFacade(
        IReadOnlyList<DeviceMonitorSnapshot> snapshots) : IMonitorSnapshotQueryFacade
    {
        public Task<List<DeviceMonitorSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(snapshots.ToList());
    }
}
