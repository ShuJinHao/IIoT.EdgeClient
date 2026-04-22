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
                "StackingLine",
                "叠片",
                "Stacking profile",
                null,
                "StackingLine",
                @"..\stack\IIoT.Edge.Shell.exe",
                "LayersTriple",
                "#0F766E")
        ];

        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场管理员")),
            new StubShellLaunchService());

        await viewModel.LoginAsync("101650", "Ljh123456!");

        Assert.True(viewModel.IsAuthenticated);
        Assert.Equal("已登录：现场管理员", viewModel.WelcomeText);
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
            new("StackingLine", "叠片", "叠片工序", null, "StackingLine", @"..\stack\IIoT.Edge.Shell.exe", "LayersTriple", "#0F766E"),
            new("InjectionLine", "注液", "注液工序", null, "InjectionLine", @"..\injection\IIoT.Edge.Shell.exe", "Syringe", "#B45309"),
            new("HomogenizationLine", "匀浆", "匀浆工序", null, "HomogenizationLine", @"..\homogenization\IIoT.Edge.Shell.exe", "BeakerOutline", "#4D7C0F")
        ];

        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog(profiles),
            new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场管理员")),
            new StubShellLaunchService());

        await viewModel.LoginAsync("101650", "Ljh123456!");
        viewModel.ProfileSearchText = "注";

        Assert.Single(viewModel.Profiles);
        Assert.Equal("注液", viewModel.Profiles[0].DisplayName);
        Assert.Equal("显示 1 / 3 个工序", viewModel.ProfileSummaryText);
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

        public StubLauncherAuthService(LauncherAuthenticationResult result)
        {
            _result = result;
        }

        public LauncherAuthenticationResult Authenticate(string? userName, string? password) => _result;
    }

    private sealed class StubShellLaunchService : IShellLaunchService
    {
        public void Launch(LauncherProfileDefinition profile)
        {
        }
    }
}
