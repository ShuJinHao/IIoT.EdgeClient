using System.Collections.ObjectModel;
using System.Reflection;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
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
    public void AlertItems_WhenMultipleDieCuttingPlcsFailSameSignal_ShouldShowOneSummary()
    {
        var store = new TestSystemLogDisplayStore();
        store.Entries.Add(CreateEntry("ERROR", "[P1-AP01] PLC 只读数据读取失败：Read R2450 failed.", second: 1));
        store.Entries.Add(CreateEntry("ERROR", "[P1-AP02] PLC 只读数据读取失败：Read R2450 failed.", second: 2));

        var languageService = new TestAppLanguageService();
        var viewModel = new DashboardPreviewRuntimeViewModel(
            new DashboardViewModel(new TestEquipmentPanelService(), languageService),
            languageService,
            store,
            new SystemLogDisplayProjector(),
            new TestRuntimeConfigService(),
            new TestEdgeSyncDiagnosticsQuery(),
            new TestPlcConnectionManager());

        typeof(DashboardPreviewRuntimeViewModel)
            .GetMethod("RefreshAlertsFromLogStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, null);

        var alert = Assert.Single(viewModel.AlertItems);
        Assert.Contains("负极模切采样异常", alert.Message, StringComparison.Ordinal);
        Assert.Contains("2 台 PLC", alert.Message, StringComparison.Ordinal);
        Assert.Contains("失败信号 R2450", alert.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("[P1-AP01]", alert.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("[P1-AP02]", alert.Message, StringComparison.Ordinal);
    }

    private static LogEntry CreateEntry(string level, string message, int second)
        => new()
        {
            Time = new DateTime(2026, 6, 24, 16, 24, second),
            Level = level,
            Message = message
        };

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
