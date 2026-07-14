using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherMainViewModelTests
{
    [Fact]
    public async Task LoginAsync_WhenAuthenticationSucceeds_ShouldLoadProfilesAndSetState()
    {
        var profiles = new[]
        {
            Profile("shell", "Shell"),
            Profile("simulator", "Simulator")
        };
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
            Profile("HomogenizationLine", "匀浆"),
            Profile("TestPluginAlphaLine", "测试插件甲"),
            Profile("TestPluginBetaLine", "测试插件乙")
        };
        var viewModel = new LauncherMainViewModel(
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
        Assert.DoesNotContain(viewModel.Profiles, card => card.DisplayName == "匀浆");
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
    public async Task CheckForUpdatesAsync_WhenSourceIsMissing_ShouldExposeNotConfiguredState()
    {
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog(Array.Empty<LauncherProfileDefinition>()),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            new StubLauncherUpdateService(
                new EdgeHostUpdateCheckResult(EdgeHostUpdateCheckState.NotConfigured)));

        await viewModel.HostUpdatePanel.CheckForUpdatesAsync();

        Assert.Contains("Launcher_Update_StatusNotConfigured", viewModel.HostUpdatePanel.StatusMessage);
        Assert.True(viewModel.HostUpdatePanel.CanCheckUpdates);
        Assert.False(viewModel.HostUpdatePanel.CanApplyUpdate);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenUpdateExists_ShouldEnableApplyUpdate()
    {
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog(Array.Empty<LauncherProfileDefinition>()),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            new StubLauncherUpdateService(
                new EdgeHostUpdateCheckResult(
                    EdgeHostUpdateCheckState.UpdateAvailable,
                    CurrentVersion: "0.0.1",
                    TargetVersion: "0.0.2",
                    ReleaseNotes: "update notes")));

        await viewModel.HostUpdatePanel.CheckForUpdatesAsync();

        Assert.Contains("0.0.2", viewModel.HostUpdatePanel.StatusMessage);
        Assert.Equal("update notes", viewModel.HostUpdatePanel.DetailText);
        Assert.True(viewModel.HostUpdatePanel.CanApplyUpdate);
    }

    [Fact]
    public async Task ApplyUpdateAsync_WhenShellIsRunning_ShouldNotApplyUpdate()
    {
        var updateService = new StubLauncherUpdateService(
            new EdgeHostUpdateCheckResult(
                EdgeHostUpdateCheckState.UpdateAvailable,
                CurrentVersion: "0.0.1",
                TargetVersion: "0.0.2"));
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog(Array.Empty<LauncherProfileDefinition>()),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(hasRunningShellProcess: true),
            updateService);

        await viewModel.HostUpdatePanel.CheckForUpdatesAsync();
        await viewModel.HostUpdatePanel.ApplyUpdateAsync();

        Assert.Contains("Launcher_Update_StatusShellRunning", viewModel.HostUpdatePanel.StatusMessage);
        Assert.Equal(0, updateService.ApplyCallCount);
        Assert.True(viewModel.HostUpdatePanel.CanApplyUpdate);
    }

    [Fact]
    public async Task LoginAsync_WhenVersionReportFails_ShouldStillAuthenticateAndLoadProfiles()
    {
        var profile = Profile("shell", "Shell");
        var releaseService = new ThrowingClientReleaseService();
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
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
    public async Task LoginAsync_WhenUpdatesExist_ShouldOnlyPopulateUpdateCenterAndNotInstallOrApply()
    {
        var profile = Profile("shell", "Shell");
        var updateService = new StubLauncherUpdateService(new EdgeHostUpdateCheckResult(
            EdgeHostUpdateCheckState.UpdateAvailable,
            CurrentVersion: "1.0.0",
            TargetVersion: "1.1.0"));
        var releaseService = new RecordingUpdateClientReleaseService(
            CreateReleaseCheckWithPluginUpdate());
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            updateService,
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForCheckAsync();

        Assert.Equal(0, updateService.ApplyCallCount);
        Assert.Equal(0, releaseService.InstallCallCount);
        Assert.Single(viewModel.Profiles);
        Assert.NotNull(viewModel.SelectedUpdateProfile);
        Assert.Contains(viewModel.ClientReleasePanel.Components, component => component.ModuleId == "IIoT.Edge.Module.Homogenization");
        Assert.Contains(viewModel.UpdateRows, row => row.ModuleId == "IIoT.Edge.Module.Homogenization");
    }

    [Fact]
    public async Task LoginAsync_WhenCatalogContainsHostAndPlugins_ShouldExposeSingleUpdateRowsTable()
    {
        var profile = Profile("shell", "Shell");
        var releaseService = new RecordingUpdateClientReleaseService(
            CreateReleaseCheckWithMultiplePluginRows());
        var viewModel = new LauncherMainViewModel(
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
        var homogenization = Assert.Single(viewModel.UpdateRows, row => row.ModuleId == "IIoT.Edge.Module.Homogenization");
        Assert.Equal("1.1.0", homogenization.TargetVersion);
        Assert.True(homogenization.CanInstallOrUpdate);
        Assert.NotNull(homogenization.VersionOption);
        Assert.Equal("插件", homogenization.ComponentKindText);
        Assert.Equal("2.0 KB", homogenization.PackageSizeDisplayText);
        Assert.Equal("均浆 release 1.1.0", homogenization.ReleaseNotesText);
        Assert.Equal(CatalogPublishedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), homogenization.PublishedAtText);
        Assert.Equal(1, homogenization.HistoryCount);
        Assert.Equal("查看 1", homogenization.HistoryActionText);
        Assert.NotNull(homogenization.VersionComponent);
        var coating = Assert.Single(viewModel.UpdateRows, row => row.ModuleId == "IIoT.Edge.Module.Coating");
        Assert.False(coating.CanInstallOrUpdate);
        Assert.Null(coating.VersionOption);
        Assert.True(coating.HasVersionHistory);
        Assert.True(coating.HasNoInstallOrUpdate);
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
        var viewModel = new LauncherMainViewModel(
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
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog([profile]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            new StubShellLaunchService(),
            clientReleaseService: releaseService);

        await viewModel.LoginAsync("operator", "secret");
        await releaseService.WaitForCheckAsync();
        await WaitUntilAsync(() => viewModel.UpdateRows.Any(row => row.ModuleId == "IIoT.Edge.Module.Homogenization"));
        var row = Assert.Single(viewModel.UpdateRows, item => item.ModuleId == "IIoT.Edge.Module.Homogenization");

        await viewModel.ExecuteUpdateRowActionAsync(row);

        Assert.Equal(1, releaseService.InstallCallCount);
        Assert.Equal(["IIoT.Edge.Module.Homogenization"], releaseService.InstalledModuleIds);
    }

    [Fact]
    public async Task LaunchProfileCardAsync_WhenPluginUpdateExists_ShouldLaunchWithoutInstallingPlugin()
    {
        var profile = Profile("shell", "Shell");
        var launchService = new StubShellLaunchService();
        var releaseService = new RecordingUpdateClientReleaseService(
            CreateReleaseCheckWithPluginUpdate());
        var viewModel = new LauncherMainViewModel(
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
        var anode = Profile("TestPluginAlphaLine", "测试插件甲");
        var cathode = Profile("TestPluginBetaLine", "测试插件乙");
        var launchService = new StubShellLaunchService(runningMachineProfiles: ["TestPluginAlphaLine"]);
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog([anode, cathode]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService);
        var cathodeCard = new LauncherProfileCardViewModel(cathode);

        await viewModel.LaunchProfileCardAsync(cathodeCard);

        Assert.Equal(1, launchService.LaunchCallCount);
        Assert.Equal(["TestPluginBetaLine"], launchService.LaunchedMachineProfiles);
    }

    [Fact]
    public async Task LaunchProfileCardAsync_WhenSameProfileIsRunning_ShouldNotLaunchAgain()
    {
        var anode = Profile("TestPluginAlphaLine", "测试插件甲");
        var launchService = new StubShellLaunchService(runningMachineProfiles: ["TestPluginAlphaLine"]);
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog([anode]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService);
        var anodeCard = new LauncherProfileCardViewModel(anode);

        await viewModel.LaunchProfileCardAsync(anodeCard);

        Assert.Equal(0, launchService.LaunchCallCount);
    }

    [Fact]
    public async Task LaunchProfileCardAsync_WhenCardWasRunningButProfileStopped_ShouldAllowRelaunch()
    {
        var anode = Profile("TestPluginAlphaLine", "测试插件甲");
        var launchService = new StubShellLaunchService();
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog([anode]),
            new StubLocalAccountAuthService(
                LauncherAuthenticationResult.Passed(Account("operator", "operator"))),
            launchService);
        var anodeCard = new LauncherProfileCardViewModel(anode);
        anodeCard.SetRunning();

        Assert.True(anodeCard.IsPrimaryActionEnabled);
        Assert.Equal(LauncherProfileCardActionKind.Launch, anodeCard.ActionKind);

        await viewModel.LaunchProfileCardAsync(anodeCard);

        Assert.Equal(1, launchService.LaunchCallCount);
        Assert.Equal(["TestPluginAlphaLine"], launchService.LaunchedMachineProfiles);
    }

    private static LauncherAccountRecord Account(string userName, string displayName) =>
        new(userName, displayName, "hash", true);

    private static readonly DateTime CatalogPublishedAtUtc = new(2026, 6, 20, 13, 45, 0, DateTimeKind.Utc);

    private static LauncherProfileDefinition Profile(string profileId, string displayName) =>
        new(profileId, displayName, "测试工序", null, profileId, "IIoT.Edge.Shell", "Shell", "#000000");

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
                    "IIoT.Edge.Module.Homogenization",
                    "均浆",
                    "1.0.0",
                    [
                        new EdgeVersionOption(
                            "1.1.0",
                            EdgeVersionStatus.Newer,
                            true,
                            null,
                            PluginRelease: new EdgePluginVersionRelease(
                        "IIoT.Edge.Module.Homogenization",
                        "均浆",
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
                    "IIoT.Edge.Module.Homogenization",
                    "均浆",
                    "1.0.0",
                    new EdgeVersionOption(
                        "1.1.0",
                        EdgeVersionStatus.Newer,
                        true,
                        null,
                        PluginRelease: CreatePluginRelease("IIoT.Edge.Module.Homogenization", "均浆", "1.1.0", 2048))),
                CreatePluginPlan(
                    "IIoT.Edge.Module.Coating",
                    "涂布",
                    "2.0.0",
                    new EdgeVersionOption(
                        "2.0.0",
                        EdgeVersionStatus.Current,
                        false,
                        null,
                        PluginRelease: CreatePluginRelease("IIoT.Edge.Module.Coating", "涂布", "2.0.0", 1024)))
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
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.True(condition(), "Timed out waiting for Launcher update rows.");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
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
        IReadOnlyList<string>? runningMachineProfiles = null) : IShellLaunchService
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

        public void Launch(LauncherProfileDefinition profile)
        {
            LaunchCallCount++;
            _launchedMachineProfiles.Add(profile.MachineProfile);
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

    private sealed class StubLauncherUpdateService(
        EdgeHostUpdateCheckResult checkResult,
        EdgeHostUpdateApplyResult? applyResult = null) : IEdgeHostUpdateService
    {
        public int ApplyCallCount { get; private set; }

        public Task<EdgeHostUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(checkResult);

        public Task<EdgeHostUpdateApplyResult> DownloadAndApplyUpdateAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            progress?.Report(100);
            return Task.FromResult(applyResult ?? new EdgeHostUpdateApplyResult(true));
        }

        public Task<EdgeHostUpdateApplyResult> ApplyVersionAsync(
            EdgeHostVersionRelease release,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            progress?.Report(100);
            return Task.FromResult(applyResult ?? new EdgeHostUpdateApplyResult(true));
        }
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

        public int InstallCallCount { get; private set; }

        public IReadOnlyList<string> InstalledModuleIds => _installedModuleIds.ToArray();

        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
        {
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
