using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Modules.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.Presentation.Shell.Localization;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class DiagnosticsViewModelBehaviorTests
{
    private static readonly DateTime TestNow = new(2026, 4, 18, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public Task DiagnosticsViewModel_ShouldExposeCloudAndMesDiagnosticsSections()
        => RunOnStaThreadAsync(async () =>
        {
            var startupStore = new FakeStartupDiagnosticsStore();
            startupStore.Update(new StartupDiagnosticsReport(
                GeneratedAt: new DateTime(2026, 4, 18, 10, 0, 0),
                ConfigurationProfile: new ConfigurationProfileSnapshot(
                    "Production",
                    "HomogenizationLine",
                    "appsettings.machine.HomogenizationLine.json",
                    true,
                    @"C:\EdgeRuntime\HomogenizationLine"),
                DiscoveredModules: ["Homogenization"],
                EnabledModules: ["Homogenization"],
                ActivatedModules: ["Homogenization"],
                PluginStates:
                [
                    new PluginLifecycleSnapshot("Homogenization", "Homogenization", "Homogenization", "1.0.0", PluginLifecycleState.Activated, "Plugin is enabled and activated.")
                ],
                ModuleRegistrations:
                [
                    new ModuleRegistrationSnapshot("Homogenization", "Homogenization", "IIoT.Edge.Module.Homogenization", true, true, true, true, true, true)
                ],
                DeviceBindings:
                [
                    new DeviceModuleBindingSnapshot("PLC-A", "Homogenization", true, true, true)
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
                        TestNow.AddMinutes(-2),
                        TestNow.AddMinutes(-5),
                        TestNow.AddMinutes(-2),
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
                        TestNow.AddMinutes(-1),
                        true,
                        TestNow.AddSeconds(-30),
                        "cloud retry count failed"),
                    new MesSyncDiagnosticsSnapshot(
                        MesRetryRuntimeState.Backoff,
                        TestNow.AddMinutes(-3),
                        TestNow.AddMinutes(-10),
                        TestNow.AddMinutes(-3),
                        "mes timeout",
                        2,
                        [
                            new MesChannelDiagnostics("Homogenization", TestNow.AddMinutes(-3), TestNow.AddMinutes(-10), "Failed", "mes timeout")
                        ],
                        true,
                        CapacityBlockedChannel.Fallback,
                        "total",
                        TestNow.AddMinutes(-2),
                        true,
                        TestNow.AddSeconds(-20),
                        "mes retry count failed"),
                    new ProductionContextPersistenceDiagnostics(2, TestNow.AddMinutes(-4)))
            };

            var viewModel = new DiagnosticsViewModel(startupStore, diagnosticsQuery, new TestAppLanguageService());

            await viewModel.RefreshAsync();

            Assert.Equal("上传门禁：存储故障", viewModel.CloudGateSummary);
            Assert.Equal("云端运行：等待恢复", viewModel.CloudRuntimeSummary);
            Assert.Equal("MES运行：退避中", viewModel.MesRuntimeSummary);
            Assert.Contains("产能阻塞：是", viewModel.CloudCapacitySummary, StringComparison.Ordinal);
            Assert.Contains("存储故障：是", viewModel.CloudPersistenceSummary, StringComparison.Ordinal);
            Assert.Contains("存储故障：是", viewModel.MesPersistenceSummary, StringComparison.Ordinal);
            Assert.Contains("损坏文件数：2", viewModel.ContextPersistenceSummary, StringComparison.Ordinal);
            Assert.Contains("机型：HomogenizationLine", viewModel.ConfigurationProfileSummary, StringComparison.Ordinal);
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
            var viewModel = new DiagnosticsViewModel(startupStore, diagnosticsQuery, new TestAppLanguageService());
            await viewModel.RefreshAsync();

            diagnosticsQuery.ResetCounters();
            diagnosticsQuery.BlockUntilReleased();

            var first = viewModel.RefreshAsync();
            await diagnosticsQuery.WaitUntilEnteredAsync();
            var second = viewModel.RefreshAsync();
            diagnosticsQuery.ReleaseBlockedCall();
            await Task.WhenAll(first, second);

            Assert.Equal(1, diagnosticsQuery.TotalCalls);
            Assert.Equal(1, diagnosticsQuery.MaxConcurrentCalls);
        });

    [Fact]
    public Task DiagnosticsViewModel_WhenLanguageChanges_ShouldRefreshVisibleSummaries()
        => WpfTestDispatcher.RunAsync(async () =>
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var tempFile = Path.Combine(Path.GetTempPath(), "edge-language-tests", Guid.NewGuid().ToString("N"), "language.json");

            try
            {
                WpfTestDispatcher.EnsureApplication();
                var languageService = new AppLanguageService(tempFile);
                languageService.Initialize();

                var startupStore = new FakeStartupDiagnosticsStore();
                startupStore.Update(new StartupDiagnosticsReport(
                    GeneratedAt: new DateTime(2026, 4, 18, 10, 0, 0),
                    ConfigurationProfile: new ConfigurationProfileSnapshot(
                        "Production",
                        "HomogenizationLine",
                        "appsettings.machine.HomogenizationLine.json",
                        true,
                        @"C:\EdgeRuntime\HomogenizationLine"),
                    DiscoveredModules: ["Homogenization"],
                    EnabledModules: ["Homogenization"],
                    ActivatedModules: ["Homogenization"],
                    PluginStates: [],
                    ModuleRegistrations: [],
                    DeviceBindings: [],
                    Issues: []));

                var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery
                {
                    Current = new EdgeSyncDiagnosticsSnapshot(
                        "PLC-A",
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
                        new ProductionContextPersistenceDiagnostics(0, null))
                };

                var viewModel = new DiagnosticsViewModel(startupStore, diagnosticsQuery, languageService);
                await viewModel.RefreshAsync();

                Assert.Equal("上传门禁：已就绪", viewModel.CloudGateSummary);
                Assert.Equal("设备：PLC-A", viewModel.DeviceSummary);
                Assert.StartsWith("环境：Production", viewModel.ConfigurationProfileSummary, StringComparison.Ordinal);

                languageService.Change(CultureInfo.GetCultureInfo("en-US"));
                await viewModel.RefreshAsync();

                Assert.Equal("Upload gate: Ready", viewModel.CloudGateSummary);
                Assert.Equal("Device: PLC-A", viewModel.DeviceSummary);
                Assert.StartsWith("Environment: Production", viewModel.ConfigurationProfileSummary, StringComparison.Ordinal);
                Assert.DoesNotContain("设备：", viewModel.DeviceSummary, StringComparison.Ordinal);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = originalCulture;
                CultureInfo.DefaultThreadCurrentUICulture = originalUiCulture;
                TryDeleteDirectory(Path.GetDirectoryName(tempFile));
            }
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

    private static void TryDeleteDirectory(string? directory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
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
        private TaskCompletionSource? _enteredGate;
        private TaskCompletionSource? _releaseGate;

        public EdgeSyncDiagnosticsSnapshot Current { get; set; } = new(
            "未知",
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

        public int MaxConcurrentCalls => _maxConcurrentCalls;

        public int TotalCalls => _totalCalls;

        public void ResetCounters()
        {
            _activeCalls = 0;
            _maxConcurrentCalls = 0;
            _totalCalls = 0;
        }

        public void BlockUntilReleased()
        {
            _enteredGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitUntilEnteredAsync()
            => _enteredGate?.Task ?? Task.CompletedTask;

        public void ReleaseBlockedCall()
            => _releaseGate?.TrySetResult();

        public async Task<EdgeSyncDiagnosticsSnapshot> GetCurrentAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _totalCalls);
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaxConcurrentCalls(active);

            try
            {
                if (_releaseGate is { } releaseGate)
                {
                    _enteredGate?.TrySetResult();
                    await releaseGate.Task.WaitAsync(ct);
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
