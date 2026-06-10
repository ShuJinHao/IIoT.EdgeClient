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
            cloudApiResolver: new StubCloudApiConfigurationResolver("injection", "hotair"));

        await viewModel.LoginAsync("operator", "secret");

        // 只显示下载时选装、已写码的工序；未选的不显示
        Assert.Equal(2, viewModel.Profiles.Count);
        Assert.Contains(viewModel.Profiles, card => card.DisplayName == "注液");
        Assert.Contains(viewModel.Profiles, card => card.DisplayName == "热风");
        Assert.DoesNotContain(viewModel.Profiles, card => card.DisplayName == "焊接");
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
            cloudApiResolver: new StubCloudApiConfigurationResolver());

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
                new LauncherUpdateCheckResult(LauncherUpdateCheckState.NotConfigured)));

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
                new LauncherUpdateCheckResult(
                    LauncherUpdateCheckState.UpdateAvailable,
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
            new LauncherUpdateCheckResult(
                LauncherUpdateCheckState.UpdateAvailable,
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
        var updateService = new StubLauncherUpdateService(new LauncherUpdateCheckResult(
            LauncherUpdateCheckState.UpdateAvailable,
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
        Assert.Single(viewModel.ClientReleasePanel.Plugins);
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
            new LauncherUpdateCheckResult(LauncherUpdateCheckState.NoUpdate),
            CreateReleaseCheckWithPluginUpdate());

        await viewModel.LaunchProfileCardAsync(card);

        Assert.Empty(releaseService.InstalledModuleIds);
        Assert.Equal(1, launchService.LaunchCallCount);
    }

    private static LauncherAccountRecord Account(string userName, string displayName) =>
        new(userName, displayName, "hash", true);

    private static LauncherProfileDefinition Profile(string profileId, string displayName) =>
        new(profileId, displayName, "测试工序", null, "default", "IIoT.Edge.Shell", "Shell", "#000000");

    private static LauncherClientReleaseCheckResult CreateReleaseCheckWithPluginUpdate()
        => new(
            LauncherClientReleaseCheckState.Succeeded,
            "stable",
            "win-x64",
            "1.0.0",
            "1.0.0",
            null,
            [
                new LauncherPluginUpdatePlan(
                    new LauncherClientPluginRelease(
                        Guid.NewGuid(),
                        "IIoT.Edge.Module.Homogenization",
                        "均浆",
                        null,
                        null,
                        null,
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
                        null,
                        [],
                        "published",
                        null,
                        null,
                        DateTime.UtcNow,
                        DateTime.UtcNow),
                    new LauncherInstalledPlugin(
                        "IIoT.Edge.Module.Homogenization",
                        "HomogenizationLine",
                        "均浆",
                        "1.0.0",
                        "1.0.0",
                        "1.0.0",
                        "99.0.0",
                        [],
                        "/tmp/plugin.json",
                        "/tmp/plugin"),
                    LauncherPluginUpdateState.UpdateAvailable,
                    null)
            ]);

    private sealed class StubLauncherProfileCatalog(IReadOnlyList<LauncherProfileDefinition> profiles)
        : ILauncherProfileCatalog
    {
        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => profiles;
    }

    private sealed class StubCloudApiConfigurationResolver(params string[] provisionedProfileIds)
        : ILauncherCloudApiConfigurationResolver
    {
        private readonly HashSet<string> _provisioned = new(provisionedProfileIds, StringComparer.Ordinal);

        public LauncherCloudApiConfigurationResult Resolve(LauncherProfileDefinition profile)
            => _provisioned.Contains(profile.ProfileId)
                ? LauncherCloudApiConfigurationResult.Succeeded(new LauncherCloudApiOptions(
                    "http://cloud.local",
                    10,
                    "DEV-CODE",
                    "secret",
                    "/api/v1/bootstrap/device-instance",
                    "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                    "/api/v1/edge/client-releases/version-reports"))
                : LauncherCloudApiConfigurationResult.Failed("未配置");

        public LauncherClientReleaseOptions ResolveReleaseOptions() => new("stable", "win-x64");
    }

    private sealed class StubShellLaunchService(bool hasRunningShellProcess = false) : IShellLaunchService
    {
        public bool HasRunningShellProcess { get; } = hasRunningShellProcess;
        public int LaunchCallCount { get; private set; }

        public void Launch(LauncherProfileDefinition profile)
        {
            LaunchCallCount++;
        }
    }

    private sealed class StubLocalAccountAuthService(
        LauncherAuthenticationResult loginResult,
        LauncherPasswordChangeResult? passwordChangeResult = null) : ILocalLauncherAuthService
    {
        public LauncherAuthenticationResult Authenticate(string? userName, string? password)
        {
            return loginResult;
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
        LauncherUpdateCheckResult checkResult,
        LauncherUpdateApplyResult? applyResult = null) : ILauncherUpdateService
    {
        public int ApplyCallCount { get; private set; }

        public Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(checkResult);

        public Task<LauncherUpdateApplyResult> DownloadAndApplyUpdateAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            progress?.Report(100);
            return Task.FromResult(applyResult ?? new LauncherUpdateApplyResult(true));
        }
    }

    private sealed class ThrowingClientReleaseService : ILauncherClientReleaseService
    {
        private readonly TaskCompletionSource _reportAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LauncherClientReleaseCheckResult> CheckAsync(
            LauncherProfileDefinition profile,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LauncherClientReleaseCheckResult(
                LauncherClientReleaseCheckState.NotConfigured,
                "stable",
                "win-x64",
                "1.0.0",
                "1.0.0",
                null,
                [],
                "not configured"));

        public Task<LauncherPluginInstallResult> InstallOrUpdatePluginAsync(
            LauncherProfileDefinition profile,
            string moduleId,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LauncherPluginInstallResult.Failed("not configured"));

        public Task<LauncherVersionReportResult> ReportCurrentVersionsAsync(
            LauncherProfileDefinition profile,
            CancellationToken cancellationToken = default)
        {
            _reportAttempted.TrySetResult();
            throw new InvalidOperationException("report failed");
        }

        public async Task WaitForReportAttemptAsync()
            => await _reportAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class RecordingClientReleaseService(int expectedReportCount) : ILauncherClientReleaseService
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

        public Task<LauncherClientReleaseCheckResult> CheckAsync(
            LauncherProfileDefinition profile,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LauncherClientReleaseCheckResult(
                LauncherClientReleaseCheckState.Succeeded,
                "stable",
                "win-x64",
                "1.0.0",
                "1.0.0",
                null,
                []));

        public Task<LauncherPluginInstallResult> InstallOrUpdatePluginAsync(
            LauncherProfileDefinition profile,
            string moduleId,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LauncherPluginInstallResult.Failed("not configured"));

        public Task<LauncherVersionReportResult> ReportCurrentVersionsAsync(
            LauncherProfileDefinition profile,
            CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                _reportedProfileIds.Add(profile.ProfileId);
                if (_reportedProfileIds.Count >= expectedReportCount)
                {
                    _reportsCompleted.TrySetResult();
                }
            }

            return Task.FromResult(LauncherVersionReportResult.Succeeded());
        }

        public async Task WaitForReportsAsync()
            => await _reportsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class RecordingUpdateClientReleaseService(
        LauncherClientReleaseCheckResult checkResult) : ILauncherClientReleaseService
    {
        private readonly TaskCompletionSource _checkCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _installedModuleIds = [];

        public int InstallCallCount { get; private set; }

        public IReadOnlyList<string> InstalledModuleIds => _installedModuleIds.ToArray();

        public Task<LauncherClientReleaseCheckResult> CheckAsync(
            LauncherProfileDefinition profile,
            CancellationToken cancellationToken = default)
        {
            _checkCompleted.TrySetResult();
            return Task.FromResult(checkResult);
        }

        public Task<LauncherPluginInstallResult> InstallOrUpdatePluginAsync(
            LauncherProfileDefinition profile,
            string moduleId,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCallCount++;
            _installedModuleIds.Add(moduleId);
            progress?.Report(100);
            return Task.FromResult(LauncherPluginInstallResult.Succeeded([moduleId]));
        }

        public Task<LauncherVersionReportResult> ReportCurrentVersionsAsync(
            LauncherProfileDefinition profile,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LauncherVersionReportResult.Succeeded());

        public async Task WaitForCheckAsync()
            => await _checkCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
