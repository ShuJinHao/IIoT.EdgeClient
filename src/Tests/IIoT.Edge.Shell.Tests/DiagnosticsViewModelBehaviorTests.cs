using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class DiagnosticsViewModelBehaviorTests
{
    [Fact]
    public Task DiagnosticsViewModel_ShouldExposeCloudAndMesDiagnosticsSections()
        => RunOnStaThreadAsync(async () =>
        {
            var startupStore = new FakeStartupDiagnosticsStore();
            startupStore.Update(new StartupDiagnosticsReport(
                GeneratedAt: new DateTime(2026, 4, 18, 10, 0, 0),
                ConfigurationProfile: new ConfigurationProfileSnapshot("Production", "StackingLine", "appsettings.machine.StackingLine.json", true),
                DiscoveredModules: ["Injection"],
                EnabledModules: ["Injection"],
                ActivatedModules: ["Injection"],
                PluginStates:
                [
                    new PluginLifecycleSnapshot("Injection", "Injection", "Injection", "1.0.0", PluginLifecycleState.Activated, "Plugin is enabled and activated.")
                ],
                ModuleRegistrations:
                [
                    new ModuleRegistrationSnapshot("Injection", "Injection", "IIoT.Edge.Module.Injection", true, true, true, true, true, true)
                ],
                DeviceBindings:
                [
                    new DeviceModuleBindingSnapshot("PLC-A", "Injection", true, true, true)
                ],
                Issues: []));

            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery
            {
                Current = new EdgeSyncDiagnosticsSnapshot(
                    "PLC-A",
                    new CloudSyncDiagnosticsSnapshot(
                        EdgeUploadGateState.Blocked,
                        EdgeUploadBlockReason.UploadTokenRejected,
                        CloudRetryRuntimeState.WaitingForRecovery,
                        DateTime.Now.AddMinutes(-2),
                        DateTime.Now.AddMinutes(-5),
                        DateTime.Now.AddMinutes(-2),
                        CloudCallOutcome.UnauthorizedAfterRetry,
                        "upload_token_rejected",
                        "Capacity",
                        3,
                        4,
                        5,
                        true,
                        true,
                        CapacityBlockedChannel.Retry,
                        "total",
                        DateTime.Now.AddMinutes(-1),
                        true,
                        DateTime.Now.AddSeconds(-30),
                        "cloud retry count failed"),
                    new MesSyncDiagnosticsSnapshot(
                        MesRetryRuntimeState.Backoff,
                        DateTime.Now.AddMinutes(-3),
                        DateTime.Now.AddMinutes(-10),
                        DateTime.Now.AddMinutes(-3),
                        "mes timeout",
                        2,
                        [
                            new MesChannelDiagnostics("Injection", DateTime.Now.AddMinutes(-3), DateTime.Now.AddMinutes(-10), "Failed", "mes timeout")
                        ],
                        true,
                        CapacityBlockedChannel.Fallback,
                        "total",
                        DateTime.Now.AddMinutes(-2),
                        true,
                        DateTime.Now.AddSeconds(-20),
                        "mes retry count failed"),
                    new ProductionContextPersistenceDiagnostics(2, DateTime.Now.AddMinutes(-4)))
            };

            var viewModel = new DiagnosticsViewModel(startupStore, diagnosticsQuery);

            await viewModel.RefreshAsync();

            Assert.Equal("Cloud gate: Waiting for Recovery", viewModel.CloudGateSummary);
            Assert.Equal("Cloud runtime: WaitingForRecovery", viewModel.CloudRuntimeSummary);
            Assert.Equal("MES runtime: Backoff", viewModel.MesRuntimeSummary);
            Assert.Contains("Capacity blocked: yes", viewModel.CloudCapacitySummary, StringComparison.Ordinal);
            Assert.Contains("Storage fault: yes", viewModel.CloudPersistenceSummary, StringComparison.Ordinal);
            Assert.Contains("Storage fault: yes", viewModel.MesPersistenceSummary, StringComparison.Ordinal);
            Assert.Contains("Corrupt files: 2", viewModel.ContextPersistenceSummary, StringComparison.Ordinal);
            Assert.Contains("Machine profile: StackingLine", viewModel.ConfigurationProfileSummary, StringComparison.Ordinal);
            Assert.Single(viewModel.ModuleRegistrations);
            Assert.Single(viewModel.PluginStates);
            Assert.Single(viewModel.DeviceBindings);
            Assert.Single(viewModel.MesUploadDiagnostics);
        });

    [Fact]
    public Task DiagnosticsViewModel_WhenRefreshReenters_ShouldOnlyRunOneDiagnosticsQuery()
        => RunOnStaThreadAsync(async () =>
        {
            var startupStore = new FakeStartupDiagnosticsStore();
            startupStore.Update(StartupDiagnosticsReport.Empty());

            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery();
            var viewModel = new DiagnosticsViewModel(startupStore, diagnosticsQuery);
            await viewModel.RefreshAsync();

            diagnosticsQuery.ResetCounters();
            diagnosticsQuery.Delay = TimeSpan.FromMilliseconds(120);

            var first = viewModel.RefreshAsync();
            var second = viewModel.RefreshAsync();
            await Task.WhenAll(first, second);

            Assert.Equal(1, diagnosticsQuery.TotalCalls);
            Assert.Equal(1, diagnosticsQuery.MaxConcurrentCalls);
        });

    private static Task RunOnStaThreadAsync(Func<Task> testBody)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await testBody();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return completion.Task;
    }

    private sealed class FakeStartupDiagnosticsStore : IStartupDiagnosticsStore
    {
        public StartupDiagnosticsReport Current { get; private set; } = StartupDiagnosticsReport.Empty();

        public void Update(StartupDiagnosticsReport report)
        {
            Current = report;
        }
    }

    private sealed class FakeEdgeSyncDiagnosticsQuery : IEdgeSyncDiagnosticsQuery
    {
        private int _activeCalls;
        private int _maxConcurrentCalls;
        private int _totalCalls;

        public EdgeSyncDiagnosticsSnapshot Current { get; set; } = new(
            "Unknown",
            new CloudSyncDiagnosticsSnapshot(
                EdgeUploadGateState.Unknown,
                EdgeUploadBlockReason.DeviceUnidentified,
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

        public TimeSpan Delay { get; set; }

        public int MaxConcurrentCalls => _maxConcurrentCalls;

        public int TotalCalls => _totalCalls;

        public void ResetCounters()
        {
            _activeCalls = 0;
            _maxConcurrentCalls = 0;
            _totalCalls = 0;
        }

        public async Task<EdgeSyncDiagnosticsSnapshot> GetCurrentAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _totalCalls);
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaxConcurrentCalls(active);

            try
            {
                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, ct);
                }

                return Current;
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        private void UpdateMaxConcurrentCalls(int active)
        {
            while (true)
            {
                var current = _maxConcurrentCalls;
                if (active <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentCalls, active, current) == current)
                {
                    return;
                }
            }
        }
    }
}
