using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Modules.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.Presentation.Shell.Localization;
using IIoT.Edge.UI.Shared.Localization;
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
                        "cloud retry count failed",
                        PendingPassStationCount: 3),
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

            var viewModel = CreateViewModel(startupStore, diagnosticsQuery, new TestAppLanguageService());

            await viewModel.RefreshAsync();

            Assert.Equal("上传门禁：存储故障", viewModel.CloudGateSummary);
            Assert.Equal("云端运行：等待恢复", viewModel.CloudRuntimeSummary);
            Assert.Equal("待处理：过站=3，日志=4，产能=5，死信=0", viewModel.CloudPendingSummary);
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
            var viewModel = CreateViewModel(startupStore, diagnosticsQuery, new TestAppLanguageService());
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

                var viewModel = CreateViewModel(startupStore, diagnosticsQuery, languageService);
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

    [Fact]
    public Task RequeueDeadLetterCommand_WhenConfirmationCanceled_ShouldNotCallOperator()
        => RunOnStaThreadAsync(async () =>
        {
            var confirmation = new FakeDeadLetterConfirmationService
            {
                RequeueResult = false
            };
            var deadLetterOperator = new FakeDiagnosticsDeadLetterOperator();
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                new FakeEdgeSyncDiagnosticsQuery(),
                new TestAppLanguageService(),
                deadLetterOperator,
                confirmation);

            viewModel.RequeueDeadLetterCommand.Execute(CreateDeadLetterRow());
            await WaitUntilAsync(() => confirmation.RequeueCallCount == 1);

            Assert.Equal(0, deadLetterOperator.RequeueCallCount);
            Assert.Equal("已取消死信重新入队。", viewModel.StatusMessage);
        });

    [Fact]
    public Task RequeueDeadLetterCommand_WhenConfirmedAndSuccessful_ShouldCallOperatorAndRefresh()
        => RunOnStaThreadAsync(async () =>
        {
            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery();
            var deadLetterOperator = new FakeDiagnosticsDeadLetterOperator
            {
                RequeueResult = new DiagnosticsDeadLetterOperationResult(true, "重新入队成功")
            };
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                diagnosticsQuery,
                new TestAppLanguageService(),
                deadLetterOperator,
                new FakeDeadLetterConfirmationService());

            viewModel.RequeueDeadLetterCommand.Execute(CreateDeadLetterRow());
            await WaitUntilAsync(() => deadLetterOperator.RequeueCallCount == 1 && diagnosticsQuery.TotalCalls >= 1);

            Assert.Equal("重新入队成功", viewModel.StatusMessage);
            Assert.Equal(1, deadLetterOperator.RequeueCallCount);
        });

    [Fact]
    public Task DeleteDeadLetterCommand_WhenConfirmationCanceled_ShouldNotCallOperator()
        => RunOnStaThreadAsync(async () =>
        {
            var confirmation = new FakeDeadLetterConfirmationService
            {
                DeleteResult = false
            };
            var deadLetterOperator = new FakeDiagnosticsDeadLetterOperator();
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                new FakeEdgeSyncDiagnosticsQuery(),
                new TestAppLanguageService(),
                deadLetterOperator,
                confirmation);

            viewModel.DeleteDeadLetterCommand.Execute(CreateDeadLetterRow());
            await WaitUntilAsync(() => confirmation.DeleteCallCount == 1);

            Assert.Equal(0, deadLetterOperator.DeleteCallCount);
            Assert.Equal("已取消死信删除。", viewModel.StatusMessage);
        });

    [Fact]
    public Task DeadLetterCommands_WhenCurrentUserIsNotLocalAdmin_ShouldNotBeExecutable()
        => RunOnStaThreadAsync(() =>
        {
            var permissionService = new FakeClientPermissionService(isLocalAdmin: false);
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                new FakeEdgeSyncDiagnosticsQuery(),
                new TestAppLanguageService(),
                deadLetterOperator: new FakeDiagnosticsDeadLetterOperator(),
                permissionService: permissionService);
            var row = CreateDeadLetterRow();

            Assert.False(viewModel.CanOperateDeadLetters);
            Assert.False(viewModel.RequeueDeadLetterCommand.CanExecute(row));
            Assert.False(viewModel.DeleteDeadLetterCommand.CanExecute(row));
            return Task.CompletedTask;
        });

    [Fact]
    public Task DiagnosticsViewModel_WhenActivatedRepeatedly_ShouldObservePermissionStateOnce()
        => RunOnStaThreadAsync(async () =>
        {
            var permissionService = new FakeClientPermissionService(isLocalAdmin: false);
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                new FakeEdgeSyncDiagnosticsQuery(),
                new TestAppLanguageService(),
                deadLetterOperator: new FakeDiagnosticsDeadLetterOperator(),
                permissionService: permissionService);

            Assert.Equal(0, permissionService.SubscriberCount);

            await viewModel.OnActivatedAsync();
            Assert.Equal(1, permissionService.SubscriberCount);

            await viewModel.OnActivatedAsync();
            Assert.Equal(1, permissionService.SubscriberCount);

            await viewModel.OnDeactivatedAsync();
            Assert.Equal(0, permissionService.SubscriberCount);

            await viewModel.OnDeactivatedAsync();
            Assert.Equal(0, permissionService.SubscriberCount);
        });

    [Fact]
    public Task DiagnosticsViewModel_WhenDeactivated_ShouldIgnorePermissionStateChanges()
        => RunOnStaThreadAsync(async () =>
        {
            var permissionService = new FakeClientPermissionService(isLocalAdmin: false);
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                new FakeEdgeSyncDiagnosticsQuery(),
                new TestAppLanguageService(),
                deadLetterOperator: new FakeDiagnosticsDeadLetterOperator(),
                permissionService: permissionService);
            var permissionRefreshCount = 0;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DiagnosticsViewModel.CanOperateDeadLetters))
                {
                    permissionRefreshCount++;
                }
            };

            await viewModel.OnActivatedAsync();
            permissionService.SetLocalAdmin(true);
            await WaitUntilAsync(() => permissionRefreshCount == 1);

            await viewModel.OnDeactivatedAsync();
            permissionService.SetLocalAdmin(false);
            await Task.Delay(50);

            Assert.Equal(1, permissionRefreshCount);
            Assert.Equal(0, permissionService.SubscriberCount);
        });

    [Fact]
    public Task DeadLetterCommandEntry_WhenCurrentUserIsNotLocalAdmin_ShouldNotCallConfirmationOrOperator()
        => RunOnStaThreadAsync(async () =>
        {
            var confirmation = new FakeDeadLetterConfirmationService();
            var deadLetterOperator = new FakeDiagnosticsDeadLetterOperator();
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                new FakeEdgeSyncDiagnosticsQuery(),
                new TestAppLanguageService(),
                deadLetterOperator,
                confirmation,
                new FakeClientPermissionService(isLocalAdmin: false));

            await InvokeDeadLetterCommandEntryAsync(viewModel, "RequeueDeadLetterAsync");

            Assert.Equal(0, confirmation.RequeueCallCount);
            Assert.Equal(0, deadLetterOperator.RequeueCallCount);
            Assert.Equal("当前账号不是本地管理员，不能执行死信运维操作。", viewModel.ErrorMessage);
        });

    [Fact]
    public Task DeadLetterCommands_WhenPermissionChangesToLocalAdmin_ShouldBecomeExecutable()
        => RunOnStaThreadAsync(async () =>
        {
            var permissionService = new FakeClientPermissionService(isLocalAdmin: false);
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                new FakeEdgeSyncDiagnosticsQuery(),
                new TestAppLanguageService(),
                deadLetterOperator: new FakeDiagnosticsDeadLetterOperator(),
                permissionService: permissionService);
            var row = CreateDeadLetterRow();

            Assert.False(viewModel.RequeueDeadLetterCommand.CanExecute(row));
            Assert.False(viewModel.DeleteDeadLetterCommand.CanExecute(row));

            await viewModel.OnActivatedAsync();
            permissionService.SetLocalAdmin(true);

            Assert.True(viewModel.CanOperateDeadLetters);
            Assert.True(viewModel.RequeueDeadLetterCommand.CanExecute(row));
            Assert.True(viewModel.DeleteDeadLetterCommand.CanExecute(row));
            await viewModel.OnDeactivatedAsync();
        });

    [Fact]
    public Task RequeueDeadLetterCommand_WhenOperatorFails_ShouldShowError()
        => RunOnStaThreadAsync(async () =>
        {
            var deadLetterOperator = new FakeDiagnosticsDeadLetterOperator
            {
                RequeueResult = new DiagnosticsDeadLetterOperationResult(false, "重新入队失败")
            };
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                new FakeEdgeSyncDiagnosticsQuery(),
                new TestAppLanguageService(),
                deadLetterOperator,
                new FakeDeadLetterConfirmationService());

            viewModel.RequeueDeadLetterCommand.Execute(CreateDeadLetterRow());
            await WaitUntilAsync(() => deadLetterOperator.RequeueCallCount == 1);

            Assert.Equal("重新入队失败", viewModel.ErrorMessage);
            Assert.False(viewModel.HasStatus);
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

    private static DiagnosticsViewModel CreateViewModel(
        IStartupDiagnosticsStore startupStore,
        IEdgeSyncDiagnosticsQuery diagnosticsQuery,
        IAppLanguageService languageService,
        IDiagnosticsDeadLetterOperator? deadLetterOperator = null,
        IDiagnosticsDeadLetterConfirmationService? deadLetterConfirmationService = null,
        IClientPermissionService? permissionService = null)
    {
        var diagnosticsText = new LocalizedSyncDiagnosticsText(languageService);
        var displayNameResolver = new DiagnosticsModuleDisplayNameResolver(diagnosticsText);
        return new DiagnosticsViewModel(
            startupStore,
            diagnosticsQuery,
            languageService,
            displayNameResolver,
            new DiagnosticsSummaryBuilder(languageService, diagnosticsText, displayNameResolver),
            new DiagnosticsRowsBuilder(diagnosticsText, displayNameResolver),
            new DiagnosticsInitialSummaryFactory(languageService, diagnosticsText),
            new DiagnosticsRefreshCoordinator(),
            deadLetterOperator ?? new DiagnosticsDeadLetterOperator(),
            deadLetterConfirmationService ?? new FakeDeadLetterConfirmationService(),
            permissionService ?? new FakeClientPermissionService());
    }

    private static DeadLetterRow CreateDeadLetterRow()
        => new(
            DataPipelineRetryChannel.Cloud,
            10,
            "Homogenization",
            "Cloud",
            "FallbackPersist",
            "failed_cloud_records/10",
            "2026-04-18 10:30:00",
            "test",
            "{\"trayCode\":\"TRAY-10\"}");

    private static async Task InvokeDeadLetterCommandEntryAsync(DiagnosticsViewModel viewModel, string methodName)
    {
        var method = typeof(DiagnosticsViewModel).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = (Task?)method.Invoke(viewModel, [CreateDeadLetterRow()]);
        Assert.NotNull(task);
        await task;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 100; i++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate());
    }

    private sealed class FakeDeadLetterConfirmationService : IDiagnosticsDeadLetterConfirmationService
    {
        public bool RequeueResult { get; set; } = true;

        public bool DeleteResult { get; set; } = true;

        public int RequeueCallCount { get; private set; }

        public int DeleteCallCount { get; private set; }

        public bool ConfirmRequeue(DeadLetterRow row)
        {
            RequeueCallCount++;
            return RequeueResult;
        }

        public bool ConfirmDelete(DeadLetterRow row)
        {
            DeleteCallCount++;
            return DeleteResult;
        }
    }

    private sealed class FakeDiagnosticsDeadLetterOperator : IDiagnosticsDeadLetterOperator
    {
        public DiagnosticsDeadLetterOperationResult RequeueResult { get; set; } = new(true, "重新入队成功");

        public DiagnosticsDeadLetterOperationResult DeleteResult { get; set; } = new(true, "删除成功");

        public int RequeueCallCount { get; private set; }

        public int DeleteCallCount { get; private set; }

        public bool CanOperate(DeadLetterRow? row) => row is not null;

        public Task<DiagnosticsDeadLetterOperationResult> RequeueAsync(DeadLetterRow row)
        {
            RequeueCallCount++;
            return Task.FromResult(RequeueResult);
        }

        public Task<DiagnosticsDeadLetterOperationResult> DeleteAsync(DeadLetterRow row)
        {
            DeleteCallCount++;
            return Task.FromResult(DeleteResult);
        }
    }

    private sealed class FakeClientPermissionService(bool isLocalAdmin = true) : IClientPermissionService
    {
        private event Action? PermissionStateChangedCore;

        public bool CanEditParams => IsLocalAdmin;

        public bool CanEditHardware => IsLocalAdmin;

        public bool IsLocalAdmin { get; private set; } = isLocalAdmin;

        public int SubscriberCount => PermissionStateChangedCore?.GetInvocationList().Length ?? 0;

        public event Action? PermissionStateChanged
        {
            add => PermissionStateChangedCore += value;
            remove => PermissionStateChangedCore -= value;
        }

        public bool HasPermission(string permission) => IsLocalAdmin;

        public void SetLocalAdmin(bool isLocalAdmin)
        {
            IsLocalAdmin = isLocalAdmin;
            PermissionStateChangedCore?.Invoke();
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
