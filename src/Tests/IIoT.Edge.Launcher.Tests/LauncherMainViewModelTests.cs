using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherMainViewModelTests
{
    [Fact]
    public async Task LoginAsync_WhenAuthenticationSucceeds_ShouldNavigateToProfileViewAndLoadProfiles()
    {
        var profile = CreateProfile("HomogenizationLine", "匀浆", "HomogenizationLine");
        var viewModel = CreateViewModel(
            [profile],
            LauncherAuthenticationResult.Passed("operator"));

        await viewModel.LoginAsync("101650", "Ljh123456!");

        Assert.True(viewModel.IsAuthenticated);
        Assert.Same(viewModel.ProfileViewModel, viewModel.CurrentView);
        Assert.Equal("已登录：operator", viewModel.WelcomeText);
        Assert.Equal("请选择要启动的工序客户端。", viewModel.StatusMessage);
        Assert.Equal("共 1 个工序", viewModel.ProfileSummaryText);
        Assert.Single(viewModel.Profiles);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task LoginAsync_WhenAuthenticationFails_ShouldStayOnLoginView()
    {
        var viewModel = CreateViewModel(
            [],
            LauncherAuthenticationResult.Failed("账号或密码不正确。"));

        await viewModel.LoginAsync("101650", "wrong");

        Assert.False(viewModel.IsAuthenticated);
        Assert.Same(viewModel.LoginViewModel, viewModel.CurrentView);
        Assert.Equal("未登录", viewModel.WelcomeText);
        Assert.Equal("请修正账号信息后重试。", viewModel.StatusMessage);
        Assert.Equal("账号或密码不正确。", viewModel.ErrorMessage);
        Assert.Equal("共 0 个工序", viewModel.ProfileSummaryText);
        Assert.Empty(viewModel.Profiles);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ProfileSearchText_WhenUpdated_ShouldFilterProfiles()
    {
        var viewModel = CreateViewModel(
            [
                CreateProfile("HomogenizationLine", "Homogenization", "HomogenizationLine"),
                CreateProfile("MaintenanceLine", "Maintenance", "MaintenanceLine")
            ],
            LauncherAuthenticationResult.Passed("operator"));

        await viewModel.LoginAsync("101650", "Ljh123456!");
        viewModel.ProfileSearchText = "homogenization";

        Assert.Single(viewModel.Profiles);
        Assert.Equal("HomogenizationLine", viewModel.Profiles[0].ProfileId);
        Assert.Contains("1 / 2", viewModel.ProfileSummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsync_ShouldDelegateToShellLaunchService()
    {
        var profile = CreateProfile("HomogenizationLine", "匀浆", "HomogenizationLine");
        var launchService = new StubShellLaunchService();
        var viewModel = CreateViewModel(
            [profile],
            LauncherAuthenticationResult.Passed("operator"),
            launchService: launchService);

        await viewModel.LoginAsync("101650", "Ljh123456!");
        await viewModel.LaunchAsync(profile);

        Assert.Same(profile, launchService.LastProfile);
        Assert.Equal("已启动 匀浆，MachineProfile = HomogenizationLine。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoginAsync_WhenProfilesShareMachineProfile_ShouldRenderSingleProfileGroup()
    {
        var uiOnlyProfile = CreateProfile("HomogenizationLineAvalonia", "Homogenization Avalonia UI-only", "HomogenizationLine");
        var runtimeProfile = new LauncherProfileDefinition(
            "HomogenizationLineAvaloniaRuntime",
            "Homogenization Avalonia Runtime",
            "runtime profile",
            null,
            "HomogenizationLine",
            @"..\runtime\IIoT.Edge.AvaloniaShell.exe",
            "BeakerOutline",
            "#4D7C0F",
            ["--start-runtime"]);
        var launchService = new StubShellLaunchService();
        var viewModel = CreateViewModel(
            [runtimeProfile, uiOnlyProfile],
            LauncherAuthenticationResult.Passed("operator"),
            launchService: launchService);

        await viewModel.LoginAsync("101650", "Ljh123456!");

        var group = Assert.Single(viewModel.ProfileGroups);
        Assert.Equal("HomogenizationLine", group.MachineProfile);
        Assert.Same(uiOnlyProfile, group.PrimaryProfile);
        Assert.Equal("共 1 个工序", viewModel.ProfileSummaryText);

        await viewModel.ProfileViewModel.LaunchProfileCommand.ExecuteAsync(group);

        Assert.Same(uiOnlyProfile, launchService.LastProfile);
    }

    [Fact]
    public async Task NavigateToLogin_ShouldReturnToLoginViewAndClearProfiles()
    {
        var viewModel = CreateViewModel(
            [CreateProfile("HomogenizationLine", "匀浆", "HomogenizationLine")],
            LauncherAuthenticationResult.Passed("operator"));

        await viewModel.LoginAsync("101650", "Ljh123456!");
        viewModel.NavigateToLogin();

        Assert.False(viewModel.IsAuthenticated);
        Assert.Same(viewModel.LoginViewModel, viewModel.CurrentView);
        Assert.Equal("未登录", viewModel.WelcomeText);
        Assert.Equal("请先使用本地账号登录。", viewModel.StatusMessage);
        Assert.Equal("共 0 个工序", viewModel.ProfileSummaryText);
        Assert.Empty(viewModel.Profiles);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenPasswordChanged_ShouldReturnTrueAndRenderStatus()
    {
        var viewModel = CreateViewModel(
            [],
            LauncherAuthenticationResult.Passed("现场管理员"),
            LauncherPasswordChangeResult.Passed());

        var changed = await viewModel.ChangePasswordAsync("edge-admin", "123456", "654321");

        Assert.True(changed);
        Assert.Equal("本地密码已修改，请使用新密码登录。", viewModel.StatusMessage);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenPasswordChangeFails_ShouldReturnFalse()
    {
        var viewModel = CreateViewModel(
            [],
            LauncherAuthenticationResult.Passed("现场管理员"),
            LauncherPasswordChangeResult.Failed("旧密码不正确。"));

        var changed = await viewModel.ChangePasswordAsync("edge-admin", "wrong", "654321");

        Assert.False(changed);
        Assert.Equal("请修正密码信息后重试。", viewModel.StatusMessage);
        Assert.Equal("旧密码不正确。", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    private static LauncherMainViewModel CreateViewModel(
        IReadOnlyList<LauncherProfileDefinition> profiles,
        LauncherAuthenticationResult loginResult,
        LauncherPasswordChangeResult? passwordChangeResult = null,
        StubShellLaunchService? launchService = null)
    {
        var languageService = TestLanguageService.CreateZh();
        var authService = new StubLauncherAuthService(loginResult, passwordChangeResult);
        var profileCatalog = new StubLauncherProfileCatalog(profiles);
        var shellLaunchService = launchService ?? new StubShellLaunchService();

        return new LauncherMainViewModel(
            new LauncherLoginViewModel(authService, languageService),
            new LauncherProfileViewModel(profileCatalog, shellLaunchService, languageService),
            languageService);
    }

    private static LauncherProfileDefinition CreateProfile(
        string profileId,
        string displayName,
        string machineProfile)
    {
        return new LauncherProfileDefinition(
            profileId,
            displayName,
            $"{displayName} process",
            null,
            machineProfile,
            $@"..\{profileId}\IIoT.Edge.AvaloniaShell.exe",
            "BeakerOutline",
            "#4D7C0F");
    }

    private sealed class StubLauncherProfileCatalog : ILauncherProfileCatalog
    {
        private readonly IReadOnlyList<LauncherProfileDefinition> _profiles;

        public StubLauncherProfileCatalog(IReadOnlyList<LauncherProfileDefinition> profiles)
        {
            _profiles = profiles;
        }

        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => _profiles;
    }

    private sealed class StubLauncherAuthService : ILocalLauncherAuthService
    {
        private readonly LauncherAuthenticationResult _result;
        private readonly LauncherPasswordChangeResult _changeResult;

        public StubLauncherAuthService(
            LauncherAuthenticationResult result,
            LauncherPasswordChangeResult? changeResult = null)
        {
            _result = result;
            _changeResult = changeResult ?? LauncherPasswordChangeResult.Passed();
        }

        public LauncherAuthenticationResult Authenticate(string? userName, string? password) => _result;

        public LauncherPasswordChangeResult ChangePassword(string? userName, string? oldPassword, string? newPassword)
            => _changeResult;
    }

    private sealed class StubShellLaunchService : IShellLaunchService
    {
        public LauncherProfileDefinition? LastProfile { get; private set; }

        public void Launch(LauncherProfileDefinition profile)
        {
            LastProfile = profile;
        }
    }

    private sealed class TestLanguageService : IAvaloniaLanguageService
    {
        private readonly IReadOnlyDictionary<string, string> _texts;

        private TestLanguageService(IReadOnlyDictionary<string, string> texts)
        {
            _texts = texts;
        }

        public static TestLanguageService CreateZh()
        {
            return new TestLanguageService(new Dictionary<string, string>
            {
                ["Launcher_Error_LoginFailed"] = "本地登录失败。",
                ["Launcher_Error_PasswordChangeFailed"] = "本地密码修改失败。",
                ["Launcher_Meta_Architecture"] = "架构：Launcher + Shell + MachineProfile",
                ["Launcher_Meta_Maintainer"] = "维护：Edge Platform Team",
                ["Launcher_Meta_Platform"] = "标准平台 / 本地登录 / 插件加载",
                ["Launcher_Profile_LoggedOut"] = "未登录",
                ["Launcher_Profile_SummaryAll"] = "共 {0} 个工序",
                ["Launcher_Profile_SummaryFiltered"] = "显示 {0} / {1} 个工序",
                ["Launcher_Profile_SummaryZero"] = "共 0 个工序",
                ["Launcher_Profile_Welcome"] = "已登录：{0}",
                ["Launcher_Status_ChangingPassword"] = "正在修改本地密码...",
                ["Launcher_Status_LaunchFailed"] = "启动 {0} 失败。",
                ["Launcher_Status_LaunchStarted"] = "已启动 {0}，MachineProfile = {1}。",
                ["Launcher_Status_PasswordChanged"] = "本地密码已修改，请使用新密码登录。",
                ["Launcher_Status_PasswordChangeFailed"] = "本地密码修改失败。",
                ["Launcher_Status_PleaseLogin"] = "请先使用本地账号登录。",
                ["Launcher_Status_ProfileLoadFailed"] = "本地登录通过，但工序清单加载失败。",
                ["Launcher_Status_RetryAccount"] = "请修正账号信息后重试。",
                ["Launcher_Status_RetryPassword"] = "请修正密码信息后重试。",
                ["Launcher_Status_SelectProfile"] = "请选择要启动的工序客户端。",
                ["Launcher_Status_Verifying"] = "正在验证本地账号..."
            });
        }

        public string CultureName => "zh-CN";

        public string ToggleLabel => "EN";

        public event EventHandler? LanguageChanged;

        public string GetText(string key) => _texts.TryGetValue(key, out var value) ? value : key;

        public void Apply(string cultureName)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Toggle()
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
