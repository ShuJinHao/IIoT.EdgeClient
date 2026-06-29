using System.Collections.ObjectModel;
using System.Reflection;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Shared;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Presentation.Navigation.Features.Dashboard;
using IIoT.Edge.Presentation.Navigation.Features.Shell;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class DashboardPreviewRuntimeViewModelTests
{
    [Fact]
    public void PlcStatusTableItems_WhenDiagnosticsSnapshotsRefresh_ShouldShowRuntimeStatusRows()
    {
        var languageService = new TestAppLanguageService();
        var viewModel = new DashboardPreviewRuntimeViewModel(
            new DashboardViewModel(new TestEquipmentPanelService(), languageService),
            languageService,
            new DeviceSelectionService(),
            new TestRuntimeConfigService(),
            new TestEdgeSyncDiagnosticsQuery(),
            new TestPlcConnectionManager(),
            new TestMonitorConfiguredDeviceLoader());

        ApplyDiagnostics(
            viewModel,
            [
                new()
                {
                    NetworkDeviceId = 1,
                    DeviceName = "P1-AP01",
                    IsConnected = true,
                    ConnectionState = PlcConnectionState.Connected,
                    LatencyMs = 12,
                    LastConnectedAtUtc = new DateTimeOffset(2026, 6, 24, 16, 24, 1, TimeSpan.Zero)
                },
                new()
                {
                    NetworkDeviceId = 2,
                    DeviceName = "P1-AP02",
                    IsConnected = false,
                    ConnectionState = PlcConnectionState.Retrying,
                    LastError = "Read R2450 failed.",
                    LastFailureAtUtc = new DateTimeOffset(2026, 6, 24, 16, 24, 2, TimeSpan.Zero)
                }
            ]);

        Assert.Equal("1 / 2", viewModel.ConnectedDevices);
        Assert.Equal("12 ms", viewModel.PlcLatencyText);
        Assert.Equal(2, viewModel.PlcStatusTableItems.Count);
        Assert.Collection(
            viewModel.PlcStatusTableItems,
            item =>
            {
                Assert.Equal("P1-AP01", item.DeviceName);
                Assert.Equal("已连接", item.StateText);
                Assert.Equal("12 ms", item.LatencyText);
            },
            item =>
            {
                Assert.Equal("P1-AP02", item.DeviceName);
                Assert.Equal("重试中", item.StateText);
                Assert.Equal("—", item.LatencyText);
                Assert.Equal("读取失败", item.LastError);
                Assert.Equal("Read R2450 failed.", item.LastErrorDetail);
            });
        Assert.Contains(viewModel.ProductionSummaryItems, item =>
            string.Equals(item.Label?.ToString(), "通讯异常", StringComparison.Ordinal)
            && string.Equals(item.Value?.ToString(), "1/2 异常", StringComparison.Ordinal));
    }

    [Fact]
    public void PlcStatusTableItems_WhenSharedDeviceFilterChanges_ShouldShowSelectedDeviceOnly()
    {
        var store = new TestSystemLogDisplayStore();
        var selectionService = new DeviceSelectionService();
        var logViewModel = new LogViewModel(
            store,
            new SystemLogDisplayProjector(),
            new TestAppLanguageService(),
            selectionService);
        store.Entries.Add(CreateEntry("ERROR", "[P1-AP01] 读取 R2450 失败：Read R2450 failed.", second: 1));
        store.Entries.Add(CreateEntry("ERROR", "[P1-AP02] 读取 R2450 失败：Read R2450 failed.", second: 2));
        selectionService.SelectDevice("P1-AP01");

        var languageService = new TestAppLanguageService();
        var viewModel = new DashboardPreviewRuntimeViewModel(
            new DashboardViewModel(new TestEquipmentPanelService(), languageService),
            languageService,
            selectionService,
            new TestRuntimeConfigService(),
            new TestEdgeSyncDiagnosticsQuery(),
            new TestPlcConnectionManager(),
            new TestMonitorConfiguredDeviceLoader());

        ApplyDiagnostics(
            viewModel,
            [
                new() { NetworkDeviceId = 1, DeviceName = "P1-AP01", IsConnected = false, ConnectionState = PlcConnectionState.Retrying },
                new() { NetworkDeviceId = 2, DeviceName = "P1-AP02", IsConnected = false, ConnectionState = PlcConnectionState.Retrying }
            ]);

        var item = Assert.Single(viewModel.PlcStatusTableItems);
        Assert.Equal("P1-AP01", item.DeviceName);
        Assert.True(item.IsSelected);
    }

    [Fact]
    public void PlcStatusTableItems_WhenLastErrorIsLong_ShouldExposeSummaryAndDetail()
    {
        var languageService = new TestAppLanguageService();
        var viewModel = new DashboardPreviewRuntimeViewModel(
            new DashboardViewModel(new TestEquipmentPanelService(), languageService),
            languageService,
            new DeviceSelectionService(),
            new TestRuntimeConfigService(),
            new TestEdgeSyncDiagnosticsQuery(),
            new TestPlcConnectionManager(),
            new TestMonitorConfiguredDeviceLoader());

        ApplyDiagnostics(
            viewModel,
            [
                new()
                {
                    NetworkDeviceId = 1,
                    DeviceName = "P1-AP01",
                    IsConnected = false,
                    ConnectionState = PlcConnectionState.Retrying,
                    LastError = "The operation has timed out after 3s while reading R2450.",
                    LastFailureAtUtc = new DateTimeOffset(2026, 6, 24, 16, 24, 2, TimeSpan.Zero)
                }
            ]);

        var item = Assert.Single(viewModel.PlcStatusTableItems);
        Assert.Equal("通信超时", item.LastError);
        Assert.Equal("The operation has timed out after 3s while reading R2450.", item.LastErrorDetail);
        Assert.True(item.HasLastErrorDetail);

        viewModel.ShowPlcStatusDetailCommand.Execute(item);

        Assert.True(viewModel.IsPlcStatusDetailOpen);
        Assert.Same(item, viewModel.SelectedPlcStatusDetail);
    }

    [Fact]
    public void PlcStatusTableItems_WhenConfiguredPlcsExistWithoutRuntimeSnapshots_ShouldShowUncollectedRows()
    {
        var languageService = new TestAppLanguageService();
        var viewModel = new DashboardPreviewRuntimeViewModel(
            new DashboardViewModel(new TestEquipmentPanelService(), languageService),
            languageService,
            new DeviceSelectionService(),
            new TestRuntimeConfigService(),
            new TestEdgeSyncDiagnosticsQuery(),
            new TestPlcConnectionManager(),
            new TestMonitorConfiguredDeviceLoader());

        ApplyDiagnostics(
            viewModel,
            [],
            [
                CreateConfiguredPlc(1, "P1-AP01"),
                CreateConfiguredPlc(2, "P1-AP02"),
                CreateConfiguredPlc(3, "P1-AP03")
            ]);

        Assert.Equal("0 / 3", viewModel.ConnectedDevices);
        Assert.Contains(viewModel.ProductionSummaryItems, item =>
            string.Equals(item.Label?.ToString(), "通讯异常", StringComparison.Ordinal)
            && string.Equals(item.Value?.ToString(), "未采集", StringComparison.Ordinal));
        Assert.Equal(3, viewModel.PlcStatusTableItems.Count);
        Assert.All(viewModel.PlcStatusTableItems, item =>
        {
            Assert.Equal("未采集", item.StateText);
            Assert.Equal("—", item.LatencyText);
            Assert.Equal("—", item.LastConnectedText);
            Assert.Equal("—", item.LastFailureText);
            Assert.Equal("—", item.LastError);
        });
    }

    [Fact]
    public void PlcStatusTableItems_WhenRuntimeSnapshotExists_ShouldOverlayConfiguredBaseline()
    {
        var languageService = new TestAppLanguageService();
        var viewModel = new DashboardPreviewRuntimeViewModel(
            new DashboardViewModel(new TestEquipmentPanelService(), languageService),
            languageService,
            new DeviceSelectionService(),
            new TestRuntimeConfigService(),
            new TestEdgeSyncDiagnosticsQuery(),
            new TestPlcConnectionManager(),
            new TestMonitorConfiguredDeviceLoader());

        ApplyDiagnostics(
            viewModel,
            [
                new()
                {
                    NetworkDeviceId = 2,
                    DeviceName = "P1-AP02",
                    IsConnected = true,
                    ConnectionState = PlcConnectionState.Connected,
                    LatencyMs = 16,
                    LastConnectedAtUtc = new DateTimeOffset(2026, 6, 24, 16, 24, 1, TimeSpan.Zero)
                }
            ],
            [
                CreateConfiguredPlc(1, "P1-AP01"),
                CreateConfiguredPlc(2, "P1-AP02")
            ]);

        Assert.Equal("1 / 2", viewModel.ConnectedDevices);
        Assert.Equal("16 ms", viewModel.PlcLatencyText);
        Assert.Collection(
            viewModel.PlcStatusTableItems,
            item =>
            {
                Assert.Equal("P1-AP01", item.DeviceName);
                Assert.Equal("未采集", item.StateText);
                Assert.Equal("—", item.LatencyText);
            },
            item =>
            {
                Assert.Equal("P1-AP02", item.DeviceName);
                Assert.Equal("已连接", item.StateText);
                Assert.Equal("16 ms", item.LatencyText);
            });
        Assert.Contains(viewModel.ProductionSummaryItems, item =>
            string.Equals(item.Label?.ToString(), "通讯异常", StringComparison.Ordinal)
            && string.Equals(item.Value?.ToString(), "部分未采集", StringComparison.Ordinal));
    }

    private static void ApplyDiagnostics(
        DashboardPreviewRuntimeViewModel viewModel,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> snapshots,
        IReadOnlyCollection<NetworkDeviceEntity>? configuredPlcs = null)
    {
        typeof(DashboardPreviewRuntimeViewModel)
            .GetMethod("ApplyDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, [CreateDiagnostics(), snapshots, configuredPlcs ?? []]);
    }

    private static NetworkDeviceEntity CreateConfiguredPlc(int id, string deviceName)
    {
        var entity = NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, "127.0.0.1", 6000 + id);
        typeof(NetworkDeviceEntity)
            .BaseType!
            .GetProperty("Id")!
            .SetValue(entity, id);
        return entity;
    }

    private static LogEntry CreateEntry(string level, string message, int second)
        => new()
        {
            Time = new DateTime(2026, 6, 24, 16, 24, second),
            Level = level,
            Message = message
        };

    private static EdgeSyncDiagnosticsSnapshot CreateDiagnostics()
        => new(
            "P1-AP",
            new CloudSyncDiagnosticsSnapshot(
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
                null),
            new MesSyncDiagnosticsSnapshot(
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
                null),
            new ProductionContextPersistenceDiagnostics(0, null));

    private sealed class TestSystemLogDisplayStore : ISystemLogDisplayStore
    {
        public ObservableCollection<LogEntry> Entries { get; } = [];

        public void Clear() => Entries.Clear();
    }

    private sealed class TestEquipmentPanelService : IEquipmentPanelService
    {
        public Task<List<HardwareSnapshot>> GetHardwareStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<HardwareSnapshot>());

        public Task<RecipeSnapshot?> GetRecipeSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<RecipeSnapshot?>(null);

        public Task<CapacitySnapshot> GetCapacitySnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CapacitySnapshot(0, 0, 0, "0.0%", "--", 0, 0, 0, "--"));
    }

    private sealed class TestRuntimeConfigService : ILocalSystemRuntimeConfigService
    {
        public SystemRuntimeConfigSnapshot Current { get; } = SystemRuntimeConfigSnapshot.Default;

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestEdgeSyncDiagnosticsQuery : IEdgeSyncDiagnosticsQuery
    {
        public Task<EdgeSyncDiagnosticsSnapshot> GetCurrentAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("Diagnostics are not required by this test.");
    }

    private sealed class TestPlcConnectionManager : IPlcConnectionManager
    {
        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId) => null;

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses() => [];
    }

    private sealed class TestMonitorConfiguredDeviceLoader : IMonitorConfiguredDeviceLoader
    {
        public Task<IReadOnlyList<NetworkDeviceEntity>> LoadConfiguredPlcDevicesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NetworkDeviceEntity>>([]);

        public Task<IReadOnlyDictionary<int, PlcTaskBindingDeviceDto>> LoadTaskBindingsByDeviceAsync(
            IReadOnlyCollection<NetworkDeviceEntity> configuredPlcs,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<int, PlcTaskBindingDeviceDto>>(
                new Dictionary<int, PlcTaskBindingDeviceDto>());

        public bool HasRuntimeFactory(string? moduleId) => false;
    }
}
