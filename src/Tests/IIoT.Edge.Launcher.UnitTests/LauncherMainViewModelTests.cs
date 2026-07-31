using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Shared.Localization;
using Xunit;

namespace IIoT.Edge.Launcher.UnitTests;

public sealed class LauncherMainViewModelTests
{
    [Fact]
    public void Constructor_WhenCriticalUpdateDependencyIsMissing_ShouldFailClosed()
    {
        var profiles = new StubLauncherProfileCatalog([Profile("shell", "Shell")]);
        var auth = new StubLocalAccountAuthService(
            LauncherAuthenticationResult.Passed(Account("operator", "operator")));
        var launch = new StubShellLaunchService();
        var release = new NotConfiguredClientReleaseService();
        var targetFactory = new LauncherUpdateTargetFactory();
        var gate = new TestLauncherUpdateOperationGate();

        Assert.Throws<ArgumentNullException>(() => new LauncherMainViewModel(
            profiles,
            auth,
            launch,
            null!,
            targetFactory,
            gate));
        Assert.Throws<ArgumentNullException>(() => new LauncherMainViewModel(
            profiles,
            auth,
            launch,
            release,
            null!,
            gate));
        Assert.Throws<ArgumentNullException>(() => new LauncherMainViewModel(
            profiles,
            auth,
            launch,
            release,
            targetFactory,
            null!));
    }

    [Fact]
    public async Task LoginAsync_WhenAuthenticationSucceeds_ShouldLoadProfilesAndSetState()
    {
        var profiles = new[]
        {
            Profile("shell", "Shell"),
            Profile("simulator", "Simulator")
        };
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService());

        await viewModel.LoginAsync("operator", "secret");

        Assert.True(viewModel.IsAuthenticated);
        Assert.Equal(2, viewModel.Profiles.Count);
        Assert.False(viewModel.IsBusy);
        Assert.Contains("operator", viewModel.WelcomeText);
    }

    [Fact]
    public void Constructor_WhenAccountCatalogIsMissing_ShouldRequireInitialSetup()
    {
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([Profile("shell", "Shell")]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Failed("not used"),
                accountCatalogStatus: LauncherAccountCatalogStatus.Missing),
            new StubShellLaunchService());

        Assert.True(viewModel.AccountSetupRequired);
        Assert.False(viewModel.AccountCatalogCorrupt);
        Assert.False(viewModel.IsLoginMode);
        Assert.Equal("Launcher_Status_AccountSetupRequired", viewModel.StatusMessage);
    }

    [Fact]
    public void Constructor_WhenAccountCatalogIsCorrupt_ShouldBlockAutoOverwrite()
    {
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([Profile("shell", "Shell")]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Failed("not used"),
                accountCatalogStatus: LauncherAccountCatalogStatus.Corrupt),
            new StubShellLaunchService());

        Assert.False(viewModel.AccountSetupRequired);
        Assert.True(viewModel.AccountCatalogCorrupt);
        Assert.False(viewModel.IsLoginMode);
        Assert.Equal("Launcher_Status_AccountCatalogCorrupt", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InitializeLocalAccountAsync_WhenSetupSucceeds_ShouldLoadProfilesAndSetState()
    {
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(
            [
                Profile("shell", "Shell"),
                Profile("simulator", "Simulator")
            ]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Failed("not used"),
                accountCatalogStatus: LauncherAccountCatalogStatus.Missing,
                setupResult: LauncherAccountSetupResult.Passed(Account("101650", "现场启动管理员"))),
            new StubShellLaunchService());

        var initialized = await viewModel.InitializeLocalAccountAsync(
            "101650",
            "现场启动管理员",
            "NewPass123!",
            "NewPass123!");

        Assert.True(initialized);
        Assert.True(viewModel.IsAuthenticated);
        Assert.False(viewModel.AccountSetupRequired);
        Assert.False(viewModel.AccountCatalogCorrupt);
        Assert.False(viewModel.IsLoginMode);
        Assert.Equal(2, viewModel.Profiles.Count);
        Assert.Contains("现场启动管理员", viewModel.WelcomeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginAsync_ShouldOnlyShowProvisionedProfiles()
    {
        var profiles = new[]
        {
            Profile("injection", "注液"),
            Profile("hotair", "热风"),
            Profile("welding", "焊接")
        };
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            updateConfigurationProvider: new StubUpdateConfigurationProvider("injection", "hotair"));

        await viewModel.LoginAsync("operator", "secret");

        // 只显示下载时选装、已写码的工序；未选的不显示
        Assert.Equal(2, viewModel.Profiles.Count);
        Assert.Contains(viewModel.Profiles, card => card.DisplayName == "注液");
        Assert.Contains(viewModel.Profiles, card => card.DisplayName == "热风");
        Assert.DoesNotContain(viewModel.Profiles, card => card.DisplayName == "焊接");
    }

    [Fact]
    public async Task LoginAsync_ShouldUsePluginVisibilityInsteadOfCloudApiConfiguration()
    {
        var profiles = new[]
        {
            Profile("TestPluginLine", "测试插件"),
            Profile("TestPluginAlphaLine", "测试插件甲"),
            Profile("TestPluginBetaLine", "测试插件乙")
        };
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            updateConfigurationProvider: new StubUpdateConfigurationProvider(),
            profileVisibilityService: new StubProfileVisibilityService(
                "TestPluginAlphaLine",
                "TestPluginBetaLine"));

        await viewModel.LoginAsync("operator", "secret");

        Assert.Equal(2, viewModel.Profiles.Count);
        Assert.Contains(viewModel.Profiles, card => card.DisplayName == "测试插件甲");
        Assert.Contains(viewModel.Profiles, card => card.DisplayName == "测试插件乙");
        Assert.DoesNotContain(viewModel.Profiles, card => card.DisplayName == "测试插件");
    }

    [Fact]
    public async Task LoginAsync_ShouldLoadProfilesAndResolveCloudApiOffCallerThread()
    {
        var profiles = new[]
        {
            Profile("shell", "Shell")
        };
        var profileCatalog = new BlockingLauncherProfileCatalog(profiles);
        var cloudApiResolver = new ThreadRecordingUpdateConfigurationProvider("shell");
        var viewModel = CreateViewModel(
            profileCatalog,
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            updateConfigurationProvider: cloudApiResolver);
        var callerThreadId = Environment.CurrentManagedThreadId;

        var loginTask = viewModel.LoginAsync("operator", "secret");
        try
        {
            Assert.True(profileCatalog.WaitForLoadStart(), "Profile loading should start while LoginAsync is still awaiting.");
            Assert.True(viewModel.IsBusy);
            Assert.NotEqual(callerThreadId, profileCatalog.LoadThreadId);
        }
        finally
        {
            profileCatalog.ReleaseLoad();
        }

        await loginTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsAuthenticated);
        Assert.Single(viewModel.Profiles);
        Assert.DoesNotContain(callerThreadId, cloudApiResolver.ResolveThreadIds);
    }

    [Fact]
    public async Task LoginAsync_ShouldShowAllProfilesWhenNoneProvisioned()
    {
        var profiles = new[]
        {
            Profile("injection", "注液"),
            Profile("hotair", "热风")
        };
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            updateConfigurationProvider: new StubUpdateConfigurationProvider());

        await viewModel.LoginAsync("operator", "secret");

        // 一个都没配置好 → 回退显示全部，避免空屏（启动红线）
        Assert.Equal(2, viewModel.Profiles.Count);
    }

    [Fact]
    public async Task LoginAsync_WhenAuthenticationSucceeds_ShouldReportAllProfileVersionsSilently()
    {
        var profiles = new[]
        {
            Profile("shell", "Shell"),
            Profile("simulator", "Simulator")
        };
        var releaseService = new RecordingClientReleaseService(expectedReportCount: 2);
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForReportsAsync();

        Assert.Equal(["shell", "simulator"], releaseService.ReportedProfileIds);
        Assert.True(viewModel.IsAuthenticated);
    }

    [Fact]
    public async Task LoginAsync_WhenAuthenticationFails_ShouldExposeErrorMessage()
    {
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(Array.Empty<LauncherProfileDefinition>()),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Failed("账号或密码不正确。")),
            new StubShellLaunchService());

        await viewModel.LoginAsync("operator", "bad");

        Assert.False(viewModel.IsAuthenticated);
        Assert.Equal("账号或密码不正确。", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Search_ShouldFilterProfilesByName()
    {
        var profiles = new[]
        {
            Profile("shell", "Shell"),
            Profile("simulator", "Simulator")
        };
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService());

        await viewModel.LoginAsync("operator", "secret");

        viewModel.ProfileSearchText = "sim";

        Assert.Single(viewModel.Profiles);
        Assert.Equal("Simulator", viewModel.Profiles[0].DisplayName);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenAuthenticationSucceeds_ShouldClearError()
    {
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(Array.Empty<LauncherProfileDefinition>()),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator")),
                LauncherPasswordChangeResult.Passed()),
            new StubShellLaunchService());

        var changed = await viewModel.ChangePasswordAsync("operator", "old", "new-pass");

        Assert.True(changed);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenAuthenticationFails_ShouldExposeErrorMessage()
    {
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(Array.Empty<LauncherProfileDefinition>()),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator")),
                LauncherPasswordChangeResult.Failed("旧密码不正确。")),
            new StubShellLaunchService());

        var changed = await viewModel.ChangePasswordAsync("operator", "bad", "new-pass");

        Assert.False(changed);
        Assert.Equal("旧密码不正确。", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task LoginAsync_WhenVersionReportFails_ShouldStillAuthenticateAndLoadProfiles()
    {
        var profile = Profile("shell", "Shell");
        var releaseService = new ThrowingClientReleaseService();
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForReportAttemptAsync();

        Assert.True(viewModel.IsAuthenticated);
        Assert.Single(viewModel.Profiles);
        Assert.Equal("Shell", viewModel.Profiles[0].DisplayName);
    }

    [Fact]
    public async Task LaunchAsync_WhenVersionReportFails_ShouldStillLaunchShell()
    {
        var profile = Profile("shell", "Shell");
        var launchService = new StubShellLaunchService();
        var releaseService = new ThrowingClientReleaseService();
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService,
            clientReleaseService: releaseService);

        await viewModel.LaunchAsync(profile);
        await releaseService.WaitForReportAttemptAsync();

        Assert.Equal(1, launchService.LaunchCallCount);
        Assert.Contains("Shell", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchAsync_WhenShellStarts_ShouldReportSelectedProfileVersionSilently()
    {
        var profile = Profile("shell", "Shell");
        var launchService = new StubShellLaunchService();
        var releaseService = new RecordingClientReleaseService(expectedReportCount: 1);
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService,
            clientReleaseService: releaseService);

        await viewModel.LaunchAsync(profile);
        await releaseService.WaitForReportsAsync();

        Assert.Equal(1, launchService.LaunchCallCount);
        Assert.Equal(["shell"], releaseService.ReportedProfileIds);
    }

    [Fact]
    public async Task LaunchAsync_WhenShellIsReadyWithDiagnostics_ShouldShowStableReasonCodes()
    {
        var profile = Profile("shell", "Shell");
        var diagnostic = new IIoT.Edge.SharedKernel.Configuration.EdgeClientShellLaunchDiagnostic(
            "STARTUP_CLOUD_RETRY_TASK_FAILED",
            "System.Diagnostics");
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(
                launchResult: new ShellLaunchResult(true, [diagnostic])));

        await viewModel.LaunchAsync(profile);

        Assert.Contains("STARTUP_CLOUD_RETRY_TASK_FAILED", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task LaunchAsync_WhenShellThrowsSensitiveFailure_ShouldShowOnlySafeExceptionType()
    {
        const string sensitiveMessage = "secret path and token";
        var profile = Profile("shell", "Shell");
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(
                launchException: new IOException(sensitiveMessage)));

        await viewModel.LaunchAsync(profile);

        Assert.DoesNotContain(sensitiveMessage, viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(nameof(IOException), viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupDiagnostics_WhenStoreChanges_ShouldUpdateTextAndUnsubscribeOnDispose()
    {
        var diagnostics = new LauncherStartupDiagnosticStore();
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([Profile("shell", "Shell")]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            startupDiagnosticReader: diagnostics);

        Assert.False(viewModel.HasStartupDiagnostics);
        diagnostics.ReplaceArea(
            LauncherStartupDiagnosticAreas.EnabledPluginSelection,
            [
                new LauncherStartupDiagnostic(
                    LauncherStartupDiagnosticAreas.EnabledPluginSelection,
                    "LAUNCHER_PLUGIN_SELECTION_INVALID",
                    LauncherStartupDiagnosticRepairTargets.PluginSelection)
            ]);

        Assert.True(viewModel.HasStartupDiagnostics);
        Assert.Contains("LAUNCHER_PLUGIN_SELECTION_INVALID", viewModel.StartupDiagnosticsText, StringComparison.Ordinal);
        var beforeDispose = viewModel.StartupDiagnosticsText;
        viewModel.Dispose();
        diagnostics.ReplaceArea(
            LauncherStartupDiagnosticAreas.EnabledPluginSelection,
            [
                new LauncherStartupDiagnostic(
                    LauncherStartupDiagnosticAreas.EnabledPluginSelection,
                    "LAUNCHER_PLUGIN_SELECTION_MISSING",
                    LauncherStartupDiagnosticRepairTargets.PluginSelection)
            ]);
        Assert.Equal(beforeDispose, viewModel.StartupDiagnosticsText);
    }

    [Fact]
    public async Task LaunchAsync_WhenShellStartupIsPending_ShouldYieldUntilHandshakeCompletes()
    {
        var profile = Profile("shell", "Shell");
        var launchCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var launchService = new StubShellLaunchService(
            launchTask: launchCompletion.Task);
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService);

        var launchTask = viewModel.LaunchAsync(profile);

        Assert.Equal(1, launchService.LaunchCallCount);
        Assert.False(launchTask.IsCompleted);
        launchCompletion.SetResult();
        await launchTask;
        Assert.Contains("Shell", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoginAsync_WhenUpdatesExist_ShouldOnlyPopulateUpdateCenterAndNotInstallOrApply()
    {
        var profile = Profile("shell", "Shell");
        var releaseService = new RecordingUpdateClientReleaseService(
            CreateReleaseCheckWithPluginUpdate());
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForCheckAsync();

        Assert.Equal(0, releaseService.InstallCallCount);
        Assert.Equal(1, releaseService.CheckCallCount);
        Assert.Single(viewModel.Profiles);
        Assert.NotNull(viewModel.SelectedUpdateProfile);
        Assert.Contains(viewModel.ClientReleasePanel.Components, component => component.ModuleId == "IIoT.Edge.TestPlugin");
        Assert.Contains(viewModel.UpdateRows, row => row.ModuleId == "IIoT.Edge.TestPlugin");
    }

    [Fact]
    public async Task LoginAsync_WhenCatalogContainsHostAndPlugins_ShouldExposeSingleUpdateRowsTable()
    {
        var profile = Profile("shell", "Shell");
        var releaseService = new RecordingUpdateClientReleaseService(
            CreateReleaseCheckWithMultiplePluginRows());
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForCheckAsync();
        await WaitUntilAsync(() => viewModel.UpdateRows.Count == 3);

        Assert.Equal(3, viewModel.UpdateRows.Count);
        Assert.Contains(viewModel.UpdateRows, row => row.ModuleId == "Host" && !row.CanInstallOrUpdate);
        var testplugin = Assert.Single(viewModel.UpdateRows, row => row.ModuleId == "IIoT.Edge.TestPlugin");
        Assert.Equal("1.1.0", testplugin.TargetVersion);
        Assert.True(testplugin.CanInstallOrUpdate);
        Assert.NotNull(testplugin.VersionOption);
        Assert.Equal("插件", testplugin.ComponentKindText);
        Assert.Equal("2.0 KB", testplugin.PackageSizeDisplayText);
        Assert.Equal("测试插件 release 1.1.0", testplugin.ReleaseNotesText);
        Assert.Equal(CatalogPublishedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), testplugin.PublishedAtText);
        Assert.Equal(1, testplugin.HistoryCount);
        Assert.Equal("查看 1", testplugin.HistoryActionText);
        Assert.NotNull(testplugin.VersionComponent);
        var legacyPlugin = Assert.Single(viewModel.UpdateRows, row => row.ModuleId == "IIoT.Edge.TestPluginLegacy");
        Assert.False(legacyPlugin.CanInstallOrUpdate);
        Assert.Null(legacyPlugin.VersionOption);
        Assert.True(legacyPlugin.HasVersionHistory);
        Assert.True(legacyPlugin.HasNoInstallOrUpdate);
    }

    [Fact]
    public async Task LoginAsync_WhenCatalogUnavailable_ShouldShowUnableToCheckInsteadOfLatest()
    {
        var profile = Profile("AP", "负极模切") with
        {
            ExpectedModuleIds = ["AP"]
        };
        var releaseService = new RecordingUpdateClientReleaseService(
            CreateUnavailableReleaseCheck("AP", "负极模切"));
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForCheckAsync();
        await WaitUntilAsync(() => viewModel.UpdateRows.Any(row => row.ModuleId == "AP"));

        var plugin = Assert.Single(viewModel.UpdateRows, row => row.ModuleId == "AP");
        Assert.Equal("2.0.10", plugin.CurrentVersion);
        Assert.Equal("无法检查", plugin.TargetVersion);
        Assert.Equal("无法检查", plugin.StatusText);
        Assert.Equal("无法检查", plugin.ActionText);
        Assert.False(plugin.CanInstallOrUpdate);
        Assert.DoesNotContain("已最新", plugin.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginAsync_WhenApAndCp211AreAvailable_ShouldExposeRealVersionsSizesNotesAndActions()
    {
        var profile = Profile("AP", "负极模切");
        var releaseService = new RecordingUpdateClientReleaseService(
            CreateApCp211ReleaseCheck());
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForCheckAsync();
        await WaitUntilAsync(() => viewModel.UpdateRows.Count == 3);

        var host = Assert.Single(viewModel.UpdateRows, row => row.ModuleId == "Host");
        Assert.Equal("2.0.10", host.CurrentVersion);
        Assert.Equal("2.0.10", host.TargetVersion);
        Assert.Equal(EdgeVersionStatus.Current, host.State);
        Assert.False(host.CanInstallOrUpdate);
        foreach (var moduleId in new[] { "AP", "CP" })
        {
            var plugin = Assert.Single(
                viewModel.UpdateRows,
                row => row.ModuleId == moduleId);
            Assert.Equal("2.0.10", plugin.CurrentVersion);
            Assert.Equal("2.0.11", plugin.TargetVersion);
            Assert.Equal("2.0 KB", plugin.PackageSizeDisplayText);
            Assert.Contains("2.0.11", plugin.ReleaseNotesText, StringComparison.Ordinal);
            Assert.True(plugin.CanInstallOrUpdate);
            Assert.NotNull(plugin.VersionOption);
        }
    }

    [Fact]
    public async Task LoginAsync_WhenMultipleProfilesVisible_ShouldAggregateUpdateRowsAcrossProfiles()
    {
        var profiles = new[]
        {
            Profile("TestPluginAlphaLine", "测试插件甲"),
            Profile("TestPluginBetaLine", "测试插件乙")
        };
        var releaseService = new ProfileAwareClientReleaseService(
            new Dictionary<string, EdgeReleaseCatalogResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["TestPluginAlphaLine"] = CreateReleaseCheckForModule("TestPluginAlpha", "测试插件甲"),
                ["TestPluginBetaLine"] = CreateReleaseCheckForModule("TestPluginBeta", "测试插件乙")
            });
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForChecksAsync(2);
        await WaitUntilAsync(() => viewModel.UpdateRows.Count == 3);

        Assert.Equal(3, viewModel.UpdateRows.Count);
        Assert.Contains(viewModel.UpdateRows, row => row.ModuleId == "Host");
        Assert.Contains(viewModel.UpdateRows, row => row.ModuleId == "TestPluginAlpha");
        Assert.Contains(viewModel.UpdateRows, row => row.ModuleId == "TestPluginBeta");
    }

    [Fact]
    public async Task ExecuteUpdateRowActionAsync_WhenPluginRowHasUpdate_ShouldApplySelectedCatalogVersion()
    {
        var profile = Profile("shell", "Shell");
        var releaseService = new RecordingUpdateClientReleaseService(
            CreateReleaseCheckWithPluginUpdate());
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForCheckAsync();
        await WaitUntilAsync(() => viewModel.UpdateRows.Any(row => row.ModuleId == "IIoT.Edge.TestPlugin"));
        var row = Assert.Single(viewModel.UpdateRows, item => item.ModuleId == "IIoT.Edge.TestPlugin");

        await viewModel.ExecuteUpdateRowActionAsync(row);

        Assert.Equal(1, releaseService.InstallCallCount);
        Assert.Equal(["IIoT.Edge.TestPlugin"], releaseService.InstalledModuleIds);
    }

    [Fact]
    public async Task LaunchProfileCardAsync_WhenPluginUpdateExists_ShouldLaunchWithoutInstallingPlugin()
    {
        var profile = Profile("shell", "Shell");
        var launchService = new StubShellLaunchService();
        var releaseService = new RecordingUpdateClientReleaseService(
            CreateReleaseCheckWithPluginUpdate());
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService,
            clientReleaseService: releaseService);
        var card = new LauncherProfileCardViewModel(profile);
        card.ApplyCheckResult(
            new EdgeHostUpdateCheckResult(EdgeHostUpdateCheckState.NoUpdate),
            CreateReleaseCheckWithPluginUpdate());

        await viewModel.LaunchProfileCardAsync(card);

        Assert.Empty(releaseService.InstalledModuleIds);
        Assert.Equal(1, launchService.LaunchCallCount);
    }

    [Fact]
    public async Task LaunchProfileCardAsync_WhenDifferentProfileIsRunning_ShouldStillLaunchSelectedProfile()
    {
        var alphaProfile = Profile("TestPluginAlphaLine", "测试插件甲");
        var betaProfile = Profile("TestPluginBetaLine", "测试插件乙");
        var launchService = new StubShellLaunchService(runningMachineProfiles: ["TestPluginAlphaLine"]);
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([alphaProfile, betaProfile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService);
        var betaCard = new LauncherProfileCardViewModel(betaProfile);

        await viewModel.LaunchProfileCardAsync(betaCard);

        Assert.Equal(1, launchService.LaunchCallCount);
        Assert.Equal(["TestPluginBetaLine"], launchService.LaunchedMachineProfiles);
    }

    [Fact]
    public async Task LaunchProfileCardAsync_WhenSameProfileIsRunning_ShouldNotLaunchAgain()
    {
        var alphaProfile = Profile("TestPluginAlphaLine", "测试插件甲");
        var launchService = new StubShellLaunchService(runningMachineProfiles: ["TestPluginAlphaLine"]);
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([alphaProfile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService);
        var alphaCard = new LauncherProfileCardViewModel(alphaProfile);

        await viewModel.LaunchProfileCardAsync(alphaCard);

        Assert.Equal(0, launchService.LaunchCallCount);
    }

    [Fact]
    public async Task LaunchProfileCardAsync_WhenCardWasRunningButProfileStopped_ShouldAllowRelaunch()
    {
        var alphaProfile = Profile("TestPluginAlphaLine", "测试插件甲");
        var launchService = new StubShellLaunchService();
        var viewModel = CreateViewModel(
            new StubLauncherProfileCatalog([alphaProfile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService);
        var alphaCard = new LauncherProfileCardViewModel(alphaProfile);
        alphaCard.SetRunning();

        Assert.True(alphaCard.IsPrimaryActionEnabled);
        Assert.Equal(LauncherProfileCardActionKind.Launch, alphaCard.ActionKind);

        await viewModel.LaunchProfileCardAsync(alphaCard);

        Assert.Equal(1, launchService.LaunchCallCount);
        Assert.Equal(["TestPluginAlphaLine"], launchService.LaunchedMachineProfiles);
    }

    private static LauncherAccountRecord Account(string userName, string displayName) =>
        new(userName, displayName, "hash", true);

    private static readonly DateTime CatalogPublishedAtUtc = new(2026, 6, 20, 13, 45, 0, DateTimeKind.Utc);

    private static LauncherProfileDefinition Profile(string profileId, string displayName) =>
        new(profileId, displayName, "中性测试插件", null, profileId, "IIoT.Edge.Shell", "Shell", "#000000");

    private static LauncherMainViewModel CreateViewModel(
        ILauncherProfileCatalog profileCatalog,
        ILocalLauncherAuthService authService,
        IShellLaunchService launchService,
        IAppLanguageService? languageService = null,
        IEdgeReleaseService? clientReleaseService = null,
        IEdgeUpdateConfigurationProvider? updateConfigurationProvider = null,
        ILauncherUpdateTargetFactory? targetFactory = null,
        ILauncherProfileVisibilityService? profileVisibilityService = null,
        ILauncherUpdateOperationGate? updateOperationGate = null,
        ILauncherStartupDiagnosticReader? startupDiagnosticReader = null)
        => new(
            profileCatalog,
            authService,
            launchService,
            clientReleaseService ?? new NotConfiguredClientReleaseService(),
            targetFactory ?? new LauncherUpdateTargetFactory(),
            updateOperationGate ?? new TestLauncherUpdateOperationGate(),
            languageService,
            updateConfigurationProvider,
            profileVisibilityService,
            startupDiagnosticReader);

    private static EdgeReleaseCatalogResult CreateReleaseCheckWithPluginUpdate()
        => new(
            EdgeReleaseCatalogState.Succeeded,
            "stable",
            "win-x64",
            "1.0.0",
            "1.0.0",
            [
                new EdgeComponentVersionPlan(
                    EdgeComponentKind.Host,
                    "Host",
                    "Edge Host",
                    "1.0.0",
                    [
                        new EdgeVersionOption(
                            "1.0.0",
                            EdgeVersionStatus.Current,
                            false,
                            null,
                            HostRelease: new EdgeHostVersionRelease(new EdgeHostVersionEntry(
                                Guid.NewGuid(),
                                "stable",
                                "1.0.0",
                                "1.0.0",
                                "win-x64",
                                "net10.0",
                                "https://example.invalid/host.nupkg",
                                "sha256",
                                1024,
                                null,
                                "Published",
                                null,
                                null,
                                CatalogPublishedAtUtc,
                                CatalogPublishedAtUtc)))
                    ]),
                new EdgeComponentVersionPlan(
                    EdgeComponentKind.Plugin,
                    "IIoT.Edge.TestPlugin",
                    "测试插件",
                    "1.0.0",
                    [
                        new EdgeVersionOption(
                            "1.1.0",
                            EdgeVersionStatus.Newer,
                            true,
                            null,
                            PluginRelease: new EdgePluginVersionRelease(
                        "IIoT.Edge.TestPlugin",
                        "测试插件",
                        null,
                        null,
                        null,
                                new EdgePluginVersionEntry(
                                    Guid.NewGuid(),
                                    "stable",
                                    "1.1.0",
                                    "1.0.0",
                                    "1.0.0",
                                    "99.0.0",
                                    "win-x64",
                                    "net10.0",
                                    "https://example.invalid/plugin.zip",
                                    "sha256",
                                    1024,
                                    "Plugin update",
                                    [],
                                    "Published",
                                    null,
                                    null,
                                    CatalogPublishedAtUtc,
                                    CatalogPublishedAtUtc)))
                    ])
            ]);

    private static EdgeReleaseCatalogResult CreateReleaseCheckWithMultiplePluginRows()
        => new(
            EdgeReleaseCatalogState.Succeeded,
            "stable",
            "win-x64",
            "1.0.0",
            "1.0.0",
            [
                CreateHostPlan(),
                CreatePluginPlan(
                    "IIoT.Edge.TestPlugin",
                    "测试插件",
                    "1.0.0",
                    new EdgeVersionOption(
                        "1.1.0",
                        EdgeVersionStatus.Newer,
                        true,
                        null,
                        PluginRelease: CreatePluginRelease("IIoT.Edge.TestPlugin", "测试插件", "1.1.0", 2048))),
                CreatePluginPlan(
                    "IIoT.Edge.TestPluginLegacy",
                    "测试插件旧版",
                    "2.0.0",
                    new EdgeVersionOption(
                        "2.0.0",
                        EdgeVersionStatus.Current,
                        false,
                        null,
                        PluginRelease: CreatePluginRelease("IIoT.Edge.TestPluginLegacy", "测试插件旧版", "2.0.0", 1024)))
            ]);

    private static EdgeReleaseCatalogResult CreateUnavailableReleaseCheck(
        string moduleId,
        string displayName)
        => new(
            EdgeReleaseCatalogState.CatalogUnavailable,
            "stable",
            "win-x64",
            "2.0.10",
            "2.0.0",
            [
                new EdgeComponentVersionPlan(
                    EdgeComponentKind.Host,
                    "Host",
                    "Edge Host",
                    "2.0.10",
                    []),
                new EdgeComponentVersionPlan(
                    EdgeComponentKind.Plugin,
                    moduleId,
                    displayName,
                    "2.0.10",
                    [])
            ],
            "Cloud catalog 请求失败");

    private static EdgeReleaseCatalogResult CreateApCp211ReleaseCheck()
        => new(
            EdgeReleaseCatalogState.Succeeded,
            "stable",
            "win-x64",
            "2.0.10",
            "2.0.0",
            [
                new EdgeComponentVersionPlan(
                    EdgeComponentKind.Host,
                    "Host",
                    "Edge Host",
                    "2.0.10",
                    [
                        new EdgeVersionOption(
                            "2.0.10",
                            EdgeVersionStatus.Current,
                            false,
                            null,
                            HostRelease: new EdgeHostVersionRelease(
                                CreateHostEntry("2.0.10")))
                    ]),
                CreatePluginPlan(
                    "AP",
                    "负极模切",
                    "2.0.10",
                    new EdgeVersionOption(
                        "2.0.11",
                        EdgeVersionStatus.Newer,
                        true,
                        null,
                        PluginRelease: CreatePluginRelease(
                            "AP",
                            "负极模切",
                            "2.0.11",
                            2048))),
                CreatePluginPlan(
                    "CP",
                    "正极模切",
                    "2.0.10",
                    new EdgeVersionOption(
                        "2.0.11",
                        EdgeVersionStatus.Newer,
                        true,
                        null,
                        PluginRelease: CreatePluginRelease(
                            "CP",
                            "正极模切",
                            "2.0.11",
                            2048)))
            ]);

    private static EdgeReleaseCatalogResult CreateReleaseCheckForModule(string moduleId, string displayName)
        => new(
            EdgeReleaseCatalogState.Succeeded,
            "stable",
            "win-x64",
            "1.0.8",
            "1.0.0",
            [
                CreateHostPlan(),
                CreatePluginPlan(
                    moduleId,
                    displayName,
                    "1.0.0",
                    new EdgeVersionOption(
                        "1.0.0",
                        EdgeVersionStatus.Current,
                        false,
                        null,
                        PluginRelease: CreatePluginRelease(moduleId, displayName, "1.0.0", 1024)))
            ]);

    private static EdgeComponentVersionPlan CreateHostPlan()
        => new(
            EdgeComponentKind.Host,
            "Host",
            "Edge Host",
            "1.0.0",
            [
                new EdgeVersionOption(
                    "1.0.0",
                    EdgeVersionStatus.Current,
                    false,
                    null,
                    HostRelease: new EdgeHostVersionRelease(new EdgeHostVersionEntry(
                        Guid.NewGuid(),
                        "stable",
                        "1.0.0",
                        "1.0.0",
                        "win-x64",
                        "net10.0",
                        "https://example.invalid/host.nupkg",
                        "sha256",
                        1024,
                        null,
                        "Published",
                        null,
                        null,
                        CatalogPublishedAtUtc,
                        CatalogPublishedAtUtc)))
            ]);

    private static EdgeHostVersionEntry CreateHostEntry(string version)
        => new(
            Guid.NewGuid(),
            "stable",
            version,
            "2.0.0",
            "win-x64",
            "net10.0",
            $"https://example.invalid/host-{version}.nupkg",
            "sha256",
            1024,
            $"Host release {version}",
            "Published",
            null,
            null,
            CatalogPublishedAtUtc,
            CatalogPublishedAtUtc);

    private static EdgeComponentVersionPlan CreatePluginPlan(
        string moduleId,
        string displayName,
        string currentVersion,
        params EdgeVersionOption[] versions)
        => new(
            EdgeComponentKind.Plugin,
            moduleId,
            displayName,
            currentVersion,
            versions);

    private static EdgePluginVersionRelease CreatePluginRelease(
        string moduleId,
        string displayName,
        string version,
        long packageSize)
        => new(
            moduleId,
            displayName,
            null,
            null,
            null,
            new EdgePluginVersionEntry(
                Guid.NewGuid(),
                "stable",
                version,
                "1.0.0",
                "1.0.0",
                "99.0.0",
                "win-x64",
                "net10.0",
                $"https://example.invalid/{moduleId}-{version}.zip",
                "sha256",
                packageSize,
                $"{displayName} release {version}",
                [],
                "Published",
                null,
                null,
                CatalogPublishedAtUtc,
                CatalogPublishedAtUtc));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        static async Task ObserveAsync(Func<bool> observation, CancellationToken cancellationToken)
        {
            while (!observation())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        await ObserveAsync(condition, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    private sealed class StubLauncherProfileCatalog(IReadOnlyList<LauncherProfileDefinition> profiles)
        : ILauncherProfileCatalog
    {
        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => profiles;
    }

    private sealed class BlockingLauncherProfileCatalog(IReadOnlyList<LauncherProfileDefinition> profiles)
        : ILauncherProfileCatalog
    {
        private readonly ManualResetEventSlim _loadStarted = new();
        private readonly ManualResetEventSlim _allowLoad = new();

        public int LoadThreadId { get; private set; } = -1;

        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles()
        {
            LoadThreadId = Environment.CurrentManagedThreadId;
            _loadStarted.Set();
            Assert.True(_allowLoad.Wait(TimeSpan.FromSeconds(2)), "Test did not release blocked profile loading.");
            return profiles;
        }

        public bool WaitForLoadStart() => _loadStarted.Wait(TimeSpan.FromSeconds(2));

        public void ReleaseLoad() => _allowLoad.Set();
    }

    private sealed class StubUpdateConfigurationProvider(params string[] provisionedProfileIds)
        : IEdgeUpdateConfigurationProvider
    {
        private readonly HashSet<string> _provisioned = new(provisionedProfileIds, StringComparer.Ordinal);

        public EdgeUpdateConfigurationResult Resolve(EdgeUpdateTarget target)
            => _provisioned.Contains(target.MachineProfile)
                ? EdgeUpdateConfigurationResult.Succeeded(new EdgeUpdateCloudApiOptions(
                    "http://cloud.local",
                    10,
                    "DEV-CODE",
                    "secret",
                    "/api/v1/bootstrap/device-instance",
                    "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                    "/api/v1/edge/client-releases/version-reports",
                    "/api/v1/edge/runtime-heartbeats"))
                : EdgeUpdateConfigurationResult.Failed("未配置");

        public EdgeReleaseOptions ResolveReleaseOptions() => new("stable", "win-x64");
    }

    private sealed class ThreadRecordingUpdateConfigurationProvider(params string[] provisionedProfileIds)
        : IEdgeUpdateConfigurationProvider
    {
        private readonly object _syncRoot = new();
        private readonly HashSet<string> _provisioned = new(provisionedProfileIds, StringComparer.Ordinal);
        private readonly List<int> _resolveThreadIds = [];

        public IReadOnlyList<int> ResolveThreadIds
        {
            get
            {
                lock (_syncRoot)
                {
                    return _resolveThreadIds.ToArray();
                }
            }
        }

        public EdgeUpdateConfigurationResult Resolve(EdgeUpdateTarget target)
        {
            lock (_syncRoot)
            {
                _resolveThreadIds.Add(Environment.CurrentManagedThreadId);
            }

            return _provisioned.Contains(target.MachineProfile)
                ? EdgeUpdateConfigurationResult.Succeeded(new EdgeUpdateCloudApiOptions(
                    "http://cloud.local",
                    10,
                    "DEV-CODE",
                    "secret",
                    "/api/v1/bootstrap/device-instance",
                    "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                    "/api/v1/edge/client-releases/version-reports",
                    "/api/v1/edge/runtime-heartbeats"))
                : EdgeUpdateConfigurationResult.Failed("未配置");
        }

        public EdgeReleaseOptions ResolveReleaseOptions() => new("stable", "win-x64");
    }

    private sealed class StubProfileVisibilityService(params string[] visibleProfileIds)
        : ILauncherProfileVisibilityService
    {
        private readonly HashSet<string> _visibleProfileIds = new(visibleProfileIds, StringComparer.Ordinal);

        public IReadOnlyList<LauncherProfileDefinition> SelectVisibleProfiles(
            IReadOnlyList<LauncherProfileDefinition> profiles)
        {
            var visible = profiles
                .Where(profile => _visibleProfileIds.Contains(profile.ProfileId))
                .ToArray();
            return visible.Length == 0 ? profiles : visible;
        }

        public LauncherProfileSelection ResolveSelection(
            IReadOnlyList<LauncherProfileDefinition> profiles)
            => new(
                SelectVisibleProfiles(profiles),
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class StubShellLaunchService(
        bool hasRunningShellProcess = false,
        IReadOnlyList<string>? runningMachineProfiles = null,
        Task? launchTask = null,
        ShellLaunchResult? launchResult = null,
        Exception? launchException = null) : IShellLaunchService
    {
        private readonly HashSet<string> _runningMachineProfiles = new(
            runningMachineProfiles ?? [],
            StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _launchedMachineProfiles = [];

        public int LaunchCallCount { get; private set; }

        public IReadOnlyList<string> LaunchedMachineProfiles => _launchedMachineProfiles.ToArray();

        public bool HasAnyRunningShellProcess()
            => hasRunningShellProcess || _runningMachineProfiles.Count > 0;

        public bool IsProfileRunning(LauncherProfileDefinition profile)
            => _runningMachineProfiles.Contains(profile.MachineProfile);

        public async Task<ShellLaunchResult> LaunchAsync(
            LauncherProfileDefinition profile,
            CancellationToken cancellationToken = default)
        {
            LaunchCallCount++;
            _launchedMachineProfiles.Add(profile.MachineProfile);
            if (launchTask is not null)
            {
                await launchTask.WaitAsync(cancellationToken);
            }

            if (launchException is not null)
            {
                throw launchException;
            }

            return launchResult ?? new ShellLaunchResult(false, []);
        }
    }

    private sealed class StubLocalAccountAuthService(
        LauncherAuthenticationResult loginResult,
        LauncherPasswordChangeResult? passwordChangeResult = null,
        LauncherAccountCatalogStatus accountCatalogStatus = LauncherAccountCatalogStatus.Ready,
        LauncherAccountSetupResult? setupResult = null) : ILocalLauncherAuthService
    {
        public LauncherAccountCatalogStatus AccountCatalogStatus => accountCatalogStatus;

        public LauncherAuthenticationResult Authenticate(string? userName, string? password)
        {
            return loginResult;
        }

        public LauncherAccountSetupResult InitializeLocalAccount(
            string? userName,
            string? displayName,
            string? newPassword,
            string? confirmPassword)
        {
            return setupResult ?? LauncherAccountSetupResult.Passed(new LauncherAccountRecord(
                userName ?? "operator",
                displayName ?? "operator",
                "hash",
                true));
        }

        public LauncherPasswordChangeResult ChangePassword(
            string? userName,
            string? oldPassword,
            string? newPassword)
        {
            return passwordChangeResult ?? LauncherPasswordChangeResult.Passed();
        }
    }

    private sealed class TestLauncherUpdateOperationGate : ILauncherUpdateOperationGate
    {
        public IDisposable TryAcquire() => Lease.Instance;

        public IDisposable TryAcquireUpdate() => Lease.Instance;

        public string CreateShellLaunchReadyPath()
            => Path.Combine(Path.GetTempPath(), $"launcher-main-{Guid.NewGuid():N}.json");

        private sealed class Lease : IDisposable
        {
            public static Lease Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class NotConfiguredClientReleaseService : IEdgeReleaseService
    {
        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeReleaseCatalogResult(
                EdgeReleaseCatalogState.NotConfigured,
                "stable",
                "win-x64",
                string.Empty,
                string.Empty,
                [],
                "not configured"));

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not configured"));

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "not configured"));

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not configured"));

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Failed("not configured"));
    }

    private sealed class ThrowingClientReleaseService : IEdgeReleaseService
    {
        private readonly TaskCompletionSource _reportAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeReleaseCatalogResult(
                EdgeReleaseCatalogState.NotConfigured,
                "stable",
                "win-x64",
                "1.0.0",
                "1.0.0",
                [],
                "not configured"));

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not configured"));

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "not configured"));

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not configured"));

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
        {
            _reportAttempted.TrySetResult();
            throw new InvalidOperationException("report failed");
        }

        public async Task WaitForReportAttemptAsync()
            => await _reportAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class RecordingClientReleaseService(int expectedReportCount) : IEdgeReleaseService
    {
        private readonly object _syncRoot = new();
        private readonly List<string> _reportedProfileIds = [];
        private readonly TaskCompletionSource _reportsCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> ReportedProfileIds
        {
            get
            {
                lock (_syncRoot)
                {
                    return _reportedProfileIds.ToArray();
                }
            }
        }

        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeReleaseCatalogResult(
                EdgeReleaseCatalogState.Succeeded,
                "stable",
                "win-x64",
                "1.0.0",
                "1.0.0",
                []));

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not configured"));

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "not configured"));

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not configured"));

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                _reportedProfileIds.Add(target.MachineProfile);
                if (_reportedProfileIds.Count >= expectedReportCount)
                {
                    _reportsCompleted.TrySetResult();
                }
            }

            return Task.FromResult(EdgeVersionReportResult.Succeeded());
        }

        public async Task WaitForReportsAsync()
            => await _reportsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class RecordingUpdateClientReleaseService(
        EdgeReleaseCatalogResult checkResult) : IEdgeReleaseService
    {
        private readonly TaskCompletionSource _checkCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _installedModuleIds = [];

        public int CheckCallCount { get; private set; }

        public int InstallCallCount { get; private set; }

        public IReadOnlyList<string> InstalledModuleIds => _installedModuleIds.ToArray();

        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            _checkCompleted.TrySetResult();
            return Task.FromResult(checkResult);
        }

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCallCount++;
            _installedModuleIds.Add(moduleId);
            progress?.Report(100);
            return Task.FromResult(EdgePluginInstallResult.Succeeded([moduleId]));
        }

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "not configured"));

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not configured"));

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Succeeded());

        public async Task WaitForCheckAsync()
            => await _checkCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class ProfileAwareClientReleaseService(
        IReadOnlyDictionary<string, EdgeReleaseCatalogResult> checksByMachineProfile) : IEdgeReleaseService
    {
        private readonly object _syncRoot = new();
        private readonly List<string> _checkedMachineProfiles = [];
        private readonly TaskCompletionSource _checksCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                _checkedMachineProfiles.Add(target.MachineProfile);
                if (_checkedMachineProfiles.Count >= checksByMachineProfile.Count)
                {
                    _checksCompleted.TrySetResult();
                }
            }

            return Task.FromResult(checksByMachineProfile[target.MachineProfile]);
        }

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Succeeded([moduleId]));

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(true));

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not used"));

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Succeeded());

        public async Task WaitForChecksAsync(int expectedCount)
        {
            lock (_syncRoot)
            {
                if (_checkedMachineProfiles.Count >= expectedCount)
                {
                    return;
                }
            }

            await _checksCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
