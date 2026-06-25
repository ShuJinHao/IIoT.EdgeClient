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
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Presentation.Navigation.Features.Dashboard;
using IIoT.Edge.Presentation.Navigation.Features.Shell;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.SharedKernel.Context;
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
            new LogDeviceSelectionService(),
            new TestRuntimeConfigService(),
            new TestEdgeSyncDiagnosticsQuery(),
            new TestPlcConnectionManager());

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
                Assert.Equal("Read R2450 failed.", item.LastError);
            });
        Assert.Contains(viewModel.ProductionSummaryItems, item =>
            string.Equals(item.Label?.ToString(), "通讯异常", StringComparison.Ordinal)
            && string.Equals(item.Value?.ToString(), "1/2 异常", StringComparison.Ordinal));
    }

    [Fact]
    public void PlcStatusTableItems_WhenSharedDeviceFilterChanges_ShouldFilterToSelectedDevice()
    {
        var store = new TestSystemLogDisplayStore();
        var selectionService = new LogDeviceSelectionService();
        var logViewModel = new LogViewModel(
            store,
            new SystemLogDisplayProjector(),
            new TestAppLanguageService(),
            selectionService);
        store.Entries.Add(CreateEntry("ERROR", "[P1-AP01] 读取 R2450 失败：Read R2450 failed.", second: 1));
        store.Entries.Add(CreateEntry("ERROR", "[P1-AP02] 读取 R2450 失败：Read R2450 failed.", second: 2));
        logViewModel.SelectedDeviceFilter = Assert.Single(
            logViewModel.DeviceFilters,
            static option => option.Key == "P1-AP01");

        var languageService = new TestAppLanguageService();
        var viewModel = new DashboardPreviewRuntimeViewModel(
            new DashboardViewModel(new TestEquipmentPanelService(), languageService),
            languageService,
            selectionService,
            new TestRuntimeConfigService(),
            new TestEdgeSyncDiagnosticsQuery(),
            new TestPlcConnectionManager());

        ApplyDiagnostics(
            viewModel,
            [
                new() { NetworkDeviceId = 1, DeviceName = "P1-AP01", IsConnected = false, ConnectionState = PlcConnectionState.Retrying },
                new() { NetworkDeviceId = 2, DeviceName = "P1-AP02", IsConnected = false, ConnectionState = PlcConnectionState.Retrying }
            ]);

        var item = Assert.Single(viewModel.PlcStatusTableItems);
        Assert.Equal("P1-AP01", item.DeviceName);
    }

    private static void ApplyDiagnostics(
        DashboardPreviewRuntimeViewModel viewModel,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> snapshots)
    {
        typeof(DashboardPreviewRuntimeViewModel)
            .GetMethod("ApplyDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, [CreateDiagnostics(), snapshots]);
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
}
