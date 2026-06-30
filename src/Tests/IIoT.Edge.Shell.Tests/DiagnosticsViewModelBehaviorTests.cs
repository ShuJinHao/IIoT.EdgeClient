using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Modules.Diagnostics;
using System.Globalization;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.Presentation.Shell.Localization;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using Xunit;

using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Shared;
namespace IIoT.Edge.Shell.Tests;

public sealed class DiagnosticsViewModelBehaviorTests
{
    private static readonly DateTime TestNow = new(2026, 4, 18, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public Task DiagnosticsViewModel_ShouldExposeMergedSyncOperationsSection()
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

            Assert.Collection(
                viewModel.Tabs,
                tab => Assert.Equal("Diag.SyncOps", tab.Key),
                tab => Assert.Equal("Diag.Startup", tab.Key));
            Assert.True(viewModel.IsSyncOpsTabSelected);

            viewModel.SelectTabCommand.Execute(viewModel.Tabs[1]);
            Assert.True(viewModel.IsStartupTabSelected);

            await viewModel.RefreshAsync();

            Assert.Equal("上传门禁：存储故障", viewModel.CloudGateSummary);
            Assert.Equal("云端运行：等待恢复", viewModel.CloudRuntimeSummary);
            Assert.Equal("待处理：过站=3，日志=4，产能=5，死信=0", viewModel.CloudPendingSummary);
            Assert.Equal("MES运行：退避中", viewModel.MesRuntimeSummary);
            Assert.Contains("产能阻塞：是", viewModel.CloudCapacitySummary, StringComparison.Ordinal);
            Assert.Contains("存储故障：是", viewModel.CloudPersistenceSummary, StringComparison.Ordinal);
            Assert.Contains("存储故障：是", viewModel.MesPersistenceSummary, StringComparison.Ordinal);
            Assert.Contains("损坏文件数：2", viewModel.ContextPersistenceSummary, StringComparison.Ordinal);
            Assert.Equal("2", viewModel.ContextCorruptFileCount);
            Assert.NotEqual("--", viewModel.ContextLastCorruptDetectedAt);
            Assert.Contains("机型：HomogenizationLine", viewModel.ConfigurationProfileSummary, StringComparison.Ordinal);
            Assert.Equal("Production", viewModel.ConfigurationEnvironment);
            Assert.Equal("HomogenizationLine", viewModel.ConfigurationMachineProfile);
            Assert.Equal(@"C:\EdgeRuntime\HomogenizationLine", viewModel.ConfigurationRuntimeDataRoot);
            Assert.Single(viewModel.ModuleRegistrations);
            Assert.Single(viewModel.PluginStates);
            Assert.Single(viewModel.DeviceBindings);
            var readinessRow = Assert.Single(viewModel.ModuleReadinessRows);
            Assert.Equal("Homogenization", readinessRow.DisplayName);
            Assert.Equal("PLC-A", readinessRow.DeviceNames);
            Assert.True(readinessRow.ModuleRegistered);
            Assert.True(readinessRow.PluginActivated);
            Assert.True(viewModel.IsStartupHealthy);
            Assert.False(viewModel.HasStartupIssues);
            Assert.False(viewModel.IsModuleReadinessExpanded);
            Assert.True(viewModel.IsModuleReadinessCollapsed);
            Assert.Equal("展开明细", viewModel.ModuleReadinessToggleText);
            viewModel.ToggleModuleReadinessCommand.Execute(null);
            Assert.True(viewModel.IsModuleReadinessExpanded);
            Assert.Equal("收起明细", viewModel.ModuleReadinessToggleText);
            Assert.Single(viewModel.MesUploadDiagnostics);

            Assert.Equal(2, viewModel.SyncChannels.Count);
            var cloudRow = Assert.Single(viewModel.SyncChannels, x => x.Channel == "云端");
            Assert.Equal("存储故障", cloudRow.Status);
            Assert.Equal("过站=3，日志=4，产能=5", cloudRow.Pending);
            Assert.Equal(0, cloudRow.DeadLetterCount);
            Assert.Contains("重试后仍未授权", cloudRow.LastError, StringComparison.Ordinal);
            Assert.Contains("存储故障：是", cloudRow.Note, StringComparison.Ordinal);

            var mesRow = Assert.Single(viewModel.SyncChannels, x => x.Channel == "MES");
            Assert.Equal("存储故障", mesRow.Status);
            Assert.Equal("重试=2", mesRow.Pending);
            Assert.Equal(0, mesRow.DeadLetterCount);
            Assert.Equal("mes timeout", mesRow.LastError);
            Assert.Contains("存储故障：是", mesRow.Note, StringComparison.Ordinal);
        });

    [Fact]
    public Task DiagnosticsViewModel_WhenSyncChannelsAreNormal_ShouldNotExposeNormalNoiseInSyncOpsRows()
        => RunOnStaThreadAsync(async () =>
        {
            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery
            {
                Current = CreateReadySyncSnapshot()
            };
            var viewModel = CreateViewModel(new FakeStartupDiagnosticsStore(), diagnosticsQuery, new TestAppLanguageService());

            await viewModel.RefreshAsync();

            Assert.Equal(2, viewModel.SyncChannels.Count);
            foreach (var row in viewModel.SyncChannels)
            {
                Assert.Equal("--", row.LastError);
                Assert.Equal("--", row.Note);
                Assert.DoesNotContain("否", row.LastError, StringComparison.Ordinal);
                Assert.DoesNotContain("否", row.Note, StringComparison.Ordinal);
            }
        });

    [Fact]
    public Task DiagnosticsViewModel_ShouldPresentStartupIssuesAsLogRows()
        => RunOnStaThreadAsync(async () =>
        {
            var startupStore = new FakeStartupDiagnosticsStore();
            startupStore.Update(new StartupDiagnosticsReport(
                GeneratedAt: new DateTime(2026, 6, 3, 14, 0, 0),
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
                Issues:
                [
                    new StartupDiagnosticIssue(
                        "DEVICE_MODULE_MISMATCH",
                        "PLC“PLC-Homogenization-01”存在 PlcAddress 为空的 IO 映射。",
                        "Homogenization",
                        "PLC-Homogenization-01"),
                    new StartupDiagnosticIssue(
                        "HARDWARE_PROFILE_INVALID",
                        "PLC[PLC-Homogenization-01] 的信号 Test.Interaction.Manual PLC 地址不能为空。",
                        "Homogenization",
                        "PLC-Homogenization-01"),
                    new StartupDiagnosticIssue(
                        "HARDWARE_PROFILE_INVALID",
                        "PLC[PLC-Homogenization-01] 的信号 Test.Interaction.Manual PLC 地址不能为空。",
                        "Homogenization",
                        "PLC-Homogenization-01")
                ]));

            var viewModel = CreateViewModel(startupStore, new FakeEdgeSyncDiagnosticsQuery(), new TestAppLanguageService());

            await viewModel.RefreshAsync();

            Assert.True(viewModel.HasStartupIssues);
            Assert.Equal(3, viewModel.TotalIssueCount);
            Assert.Equal(2, viewModel.Issues.Count);

            var plcRow = Assert.Single(viewModel.Issues, row => row.Message.Contains("PlcAddress", StringComparison.Ordinal));
            Assert.Equal("ERROR", plcRow.LevelText);
            Assert.Equal(EdgeVisualStatus.Error, plcRow.Status);
            Assert.Equal(plcRow.Message, plcRow.DisplayMessage);
            Assert.False(plcRow.HasDuplicateCount);

            var signalRow = Assert.Single(viewModel.Issues, row => row.Message.Contains("Test.Interaction.Manual", StringComparison.Ordinal));
            Assert.Equal("信号 Test.Interaction.Manual 地址不能为空。", signalRow.Message);
            Assert.Equal("ERROR", signalRow.LevelText);
            Assert.Equal(EdgeVisualStatus.Error, signalRow.Status);
            Assert.Equal(2, signalRow.DuplicateCount);
            Assert.True(signalRow.HasDuplicateCount);
            Assert.Equal("×2", signalRow.DuplicateBadgeText);
            Assert.Equal("信号 Test.Interaction.Manual 地址不能为空。 ×2", signalRow.DisplayMessage);
        });

    [Fact]
    public Task DiagnosticsViewModel_ShouldKeepCloudAndMesDeadLettersSeparatedInOperationsTab()
        => RunOnStaThreadAsync(async () =>
        {
            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery
            {
                Current = CreateReadySyncSnapshot(
                    cloudDeadLetters: new DeadLetterDiagnosticsSnapshot(
                        1,
                        [],
                        [CreateDeadLetterRecord(101, "Cloud")],
                        false,
                        null,
                        null),
                    mesDeadLetters: new DeadLetterDiagnosticsSnapshot(
                        1,
                        [],
                        [CreateDeadLetterRecord(202, "MES")],
                        false,
                        null,
                        null))
            };
            var viewModel = CreateViewModel(new FakeStartupDiagnosticsStore(), diagnosticsQuery, new TestAppLanguageService());

            await viewModel.RefreshAsync();

            var cloudRow = Assert.Single(viewModel.CloudDeadLetters);
            Assert.Equal(DataPipelineRetryChannel.Cloud, cloudRow.Channel);
            Assert.Equal(101, cloudRow.Id);

            var mesRow = Assert.Single(viewModel.MesDeadLetters);
            Assert.Equal(DataPipelineRetryChannel.Mes, mesRow.Channel);
            Assert.Equal(202, mesRow.Id);
        });

    [Fact]
    public Task DiagnosticsViewModel_WhenDeviceSelected_ShouldFilterDeviceRowsAndKeepGlobalDiagnostics()
        => RunOnStaThreadAsync(async () =>
        {
            var startupStore = new FakeStartupDiagnosticsStore();
            startupStore.Update(new StartupDiagnosticsReport(
                GeneratedAt: new DateTime(2026, 6, 29, 9, 0, 0),
                ConfigurationProfile: new ConfigurationProfileSnapshot(
                    "Production",
                    "LineA",
                    "appsettings.machine.LineA.json",
                    true,
                    @"C:\EdgeRuntime\LineA"),
                DiscoveredModules: ["ModuleA"],
                EnabledModules: ["ModuleA"],
                ActivatedModules: ["ModuleA"],
                PluginStates: [],
                ModuleRegistrations: [],
                DeviceBindings:
                [
                    new DeviceModuleBindingSnapshot("P1-AP01", "ModuleA", true, true, true),
                    new DeviceModuleBindingSnapshot("P1-AP02", "ModuleA", true, true, true)
                ],
                Issues:
                [
                    new StartupDiagnosticIssue("PLC_A", "P1-AP01 地址缺失", "ModuleA", "P1-AP01"),
                    new StartupDiagnosticIssue("PLC_B", "P1-AP02 地址缺失", "ModuleA", "P1-AP02"),
                    new StartupDiagnosticIssue("GLOBAL", "插件配置缺失", "ModuleA")
                ]));
            var selectionService = new DeviceSelectionService();
            selectionService.SelectDevice("P1-AP01");
            var viewModel = CreateViewModel(
                startupStore,
                new FakeEdgeSyncDiagnosticsQuery(),
                new TestAppLanguageService(),
                deviceSelectionService: selectionService);

            await viewModel.RefreshAsync();

            var binding = Assert.Single(viewModel.DeviceBindings);
            Assert.Equal("P1-AP01", binding.DeviceName);
            Assert.Equal(2, viewModel.Issues.Count);
            Assert.Contains(viewModel.Issues, row => row.Message == "P1-AP01 地址缺失");
            Assert.Contains(viewModel.Issues, row => row.Message == "插件配置缺失");
            Assert.DoesNotContain(viewModel.Issues, row => row.Message == "P1-AP02 地址缺失");
            Assert.Equal(2, viewModel.TotalIssueCount);
        });

    [Fact]
    public Task DiagnosticsViewModel_WhenDeviceSelected_ShouldFilterAttributableDeadLettersAndKeepGlobalChannels()
        => RunOnStaThreadAsync(async () =>
        {
            var selectionService = new DeviceSelectionService();
            selectionService.SelectDevice("P1-AP01");
            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery
            {
                Current = CreateReadySyncSnapshot(
                    cloudDeadLetters: new DeadLetterDiagnosticsSnapshot(
                        2,
                        [],
                        [
                            CreateDeadLetterRecord(101, "Cloud-A", "P1-AP01"),
                            CreateDeadLetterRecord(102, "Cloud-B", "P1-AP02"),
                            CreateDeadLetterRecord(103, "Cloud-Global")
                        ],
                        false,
                        null,
                        null),
                    mesDeadLetters: new DeadLetterDiagnosticsSnapshot(
                        1,
                        [],
                        [CreateDeadLetterRecord(202, "MES-B", "P1-AP02")],
                        false,
                        null,
                        null))
            };
            var viewModel = CreateViewModel(
                new FakeStartupDiagnosticsStore(),
                diagnosticsQuery,
                new TestAppLanguageService(),
                deviceSelectionService: selectionService);

            await viewModel.RefreshAsync();

            Assert.Equal(2, viewModel.SyncChannels.Count);
            Assert.Equal(2, viewModel.CloudDeadLetters.Count);
            Assert.Empty(viewModel.MesDeadLetters);
            Assert.Contains(viewModel.CloudDeadLetters, row => row.FailedTarget == "Cloud-A");
            Assert.Contains(viewModel.CloudDeadLetters, row => row.FailedTarget == "Cloud-Global");
            Assert.DoesNotContain(viewModel.CloudDeadLetters, row => row.FailedTarget == "Cloud-B");
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

            Assert.False(viewModel.HasStartupReport);
            Assert.False(viewModel.IsStartupHealthy);
            Assert.False(viewModel.HasStartupIssues);

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
    public async Task DiagnosticsViewModel_WhenLanguageChanges_ShouldRefreshVisibleSummaries()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;

            try
            {
                var languageService = new BilingualDiagnosticsLanguageService();

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
            }
        }

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

    private static Task RunOnStaThreadAsync(Func<Task> testBody) => testBody();

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
        IClientPermissionService? permissionService = null,
        IDeviceSelectionService? deviceSelectionService = null)
    {
        deviceSelectionService ??= new DeviceSelectionService();
        var diagnosticsText = new LocalizedSyncDiagnosticsText(languageService);
        var displayNameResolver = new DiagnosticsModuleDisplayNameResolver(diagnosticsText);
        var collaboratorFactory = new DiagnosticsViewModelCollaboratorFactory(
            languageService,
            deadLetterOperator ?? new DiagnosticsDeadLetterOperator(),
            deadLetterConfirmationService ?? new FakeDeadLetterConfirmationService(),
            permissionService ?? new FakeClientPermissionService());

        return new DiagnosticsViewModel(
            startupStore,
            diagnosticsQuery,
            languageService,
            displayNameResolver,
            new DiagnosticsSummaryBuilder(languageService, diagnosticsText, displayNameResolver),
            new DiagnosticsRowsBuilder(languageService, diagnosticsText, displayNameResolver, deviceSelectionService),
            new DiagnosticsInitialSummaryFactory(languageService, diagnosticsText),
            new DiagnosticsRefreshCoordinator(),
            collaboratorFactory,
            deviceSelectionService);
    }

    private static EdgeSyncDiagnosticsSnapshot CreateReadySyncSnapshot(
        DeadLetterDiagnosticsSnapshot? cloudDeadLetters = null,
        DeadLetterDiagnosticsSnapshot? mesDeadLetters = null)
        => new(
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
                null,
                DeadLetters: cloudDeadLetters),
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
                null,
                DeadLetters: mesDeadLetters),
            new ProductionContextPersistenceDiagnostics(0, null));

    private static DeadLetterRecord CreateDeadLetterRecord(long id, string failedTarget, string? deviceName = null)
        => new()
        {
            Id = id,
            ProcessType = "Homogenization",
            CellDataJson = deviceName is null
                ? "{\"trayCode\":\"TRAY-10\"}"
                : $"{{\"trayCode\":\"TRAY-10\",\"deviceName\":\"{deviceName}\"}}",
            FailedTarget = failedTarget,
            SourceTable = "failed_records",
            SourceRecordId = id,
            FailureStage = "FallbackPersist",
            FailureReason = $"{failedTarget} failed",
            CreatedAt = TestNow
        };

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

        public Task<bool> ConfirmRequeueAsync(DeadLetterRow row)
        {
            RequeueCallCount++;
            return Task.FromResult(RequeueResult);
        }

        public Task<bool> ConfirmDeleteAsync(DeadLetterRow row)
        {
            DeleteCallCount++;
            return Task.FromResult(DeleteResult);
        }
    }

    private sealed class BilingualDiagnosticsLanguageService : IAppLanguageService
    {
        private readonly Dictionary<string, Dictionary<string, string>> _values = new(StringComparer.Ordinal)
        {
            ["en-US"] = new(StringComparer.Ordinal)
            {
                ["Navigation_Sync_UploadGateFormat"] = "Upload gate: {0}",
                ["Navigation_Sync_StatusReady"] = "Ready",
                ["Navigation_Diagnostics_DeviceFormat"] = "Device: {0}",
                ["Navigation_Diagnostics_ProfileWithMachineFormat"] = "Environment: {0} / Machine: {1} / Profile: {2} / Runtime: {3}"
            }
        };

        public CultureInfo Current { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");

        public LanguageOption CurrentOption => SupportedLanguages.First(x => x.Culture.Name == Current.Name);

        public IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
        [
            new(CultureInfo.GetCultureInfo("zh-CN"), "中文"),
            new(CultureInfo.GetCultureInfo("en-US"), "English")
        ];

        public event EventHandler? LanguageChanged;

        public void Initialize()
        {
        }

        public void Change(CultureInfo culture)
        {
            Current = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key, string fallback = "")
            => _values.TryGetValue(Current.Name, out var values) && values.TryGetValue(key, out var value)
                ? value
                : fallback;

        public string Format(string key, string fallback, params object[] args)
            => string.Format(CultureInfo.CurrentCulture, GetString(key, fallback), args);
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
