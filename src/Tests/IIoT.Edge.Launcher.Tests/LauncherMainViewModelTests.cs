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

    private static LauncherAccountRecord Account(string userName, string displayName) =>
        new(userName, displayName, "hash", true);

    private static LauncherProfileDefinition Profile(string profileId, string displayName) =>
        new(profileId, displayName, "测试工序", null, "default", "IIoT.Edge.Shell", "Shell", "#000000");

    private sealed class StubLauncherProfileCatalog(IReadOnlyList<LauncherProfileDefinition> profiles)
        : ILauncherProfileCatalog
    {
        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => profiles;
    }

    private sealed class StubShellLaunchService : IShellLaunchService
    {
        public void Launch(LauncherProfileDefinition profile)
        {
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
}
