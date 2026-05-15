using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherMainViewModelTests
{
    [Fact]
    public async Task LoginAsync_WhenAuthenticationSucceeds_ShouldLoadProfiles()
    {
        IReadOnlyList<LauncherProfileDefinition> profiles =
        [
            new(
                "HomogenizationLine",
                "匀浆",
                "Homogenization profile",
                null,
                "HomogenizationLine",
                @"..\homogenization\IIoT.Edge.AvaloniaShell.exe",
                "BeakerOutline",
                "#4D7C0F")
        ];

        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLauncherAuthService(LauncherAuthenticationResult.Passed("operator")),
            new StubShellLaunchService());

        await viewModel.LoginAsync("101650", "Ljh123456!");

        Assert.True(viewModel.IsAuthenticated);
        Assert.Equal("已登录：operator", viewModel.WelcomeText);
        Assert.Equal("请选择要启动的工序客户端。", viewModel.StatusMessage);
        Assert.Equal("共 1 个工序", viewModel.ProfileSummaryText);
        Assert.Single(viewModel.Profiles);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task LoginAsync_WhenAuthenticationFails_ShouldStayOnLoginState()
    {
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog([]),
            new StubLauncherAuthService(LauncherAuthenticationResult.Failed("账号或密码不正确。")),
            new StubShellLaunchService());

        await viewModel.LoginAsync("101650", "wrong");

        Assert.False(viewModel.IsAuthenticated);
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
        IReadOnlyList<LauncherProfileDefinition> profiles =
        [
            new("HomogenizationLine", "Homogenization", "Homogenization process", null, "HomogenizationLine", @"..\homogenization\IIoT.Edge.AvaloniaShell.exe", "BeakerOutline", "#4D7C0F"),
            new("MaintenanceLine", "Maintenance", "Maintenance process", null, "MaintenanceLine", @"..\maintenance\IIoT.Edge.AvaloniaShell.exe", "Cog", "#0F766E")
        ];

        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLauncherAuthService(LauncherAuthenticationResult.Passed("operator")),
            new StubShellLaunchService());

        await viewModel.LoginAsync("101650", "Ljh123456!");
        viewModel.ProfileSearchText = "homogenization";

        Assert.Single(viewModel.Profiles);
        Assert.Equal("HomogenizationLine", viewModel.Profiles[0].ProfileId);
        Assert.Contains("1 / 2", viewModel.ProfileSummaryText, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ChangePasswordAsync_WhenPasswordChanged_ShouldReturnTrueAndRenderStatus()
    {
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog([]),
            new StubLauncherAuthService(
                LauncherAuthenticationResult.Passed("现场管理员"),
                LauncherPasswordChangeResult.Passed()),
            new StubShellLaunchService());

        var changed = await viewModel.ChangePasswordAsync("edge-admin", "123456", "654321");

        Assert.True(changed);
        Assert.Equal("本地密码已修改，请使用新密码登录。", viewModel.StatusMessage);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenPasswordChangeFails_ShouldReturnFalse()
    {
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog([]),
            new StubLauncherAuthService(
                LauncherAuthenticationResult.Passed("现场管理员"),
                LauncherPasswordChangeResult.Failed("旧密码不正确。")),
            new StubShellLaunchService());

        var changed = await viewModel.ChangePasswordAsync("edge-admin", "wrong", "654321");

        Assert.False(changed);
        Assert.Equal("请修正密码信息后重试。", viewModel.StatusMessage);
        Assert.Equal("旧密码不正确。", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
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
        public void Launch(LauncherProfileDefinition profile)
        {
        }
    }
}
