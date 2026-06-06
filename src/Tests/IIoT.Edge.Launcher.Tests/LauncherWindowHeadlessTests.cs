using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherWindowHeadlessTests
{
    [AvaloniaFact]
    public void MainWindow_WhenUnauthenticated_ShouldLoadLoginView()
    {
        var window = CreateMainWindow(CreateViewModel());

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("LoginPageRoot")?.IsVisible);
            Assert.False(window.FindControl<Control>("SelectionPageRoot")?.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenAuthenticated_ShouldLoadSelectionView()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoginAsync("operator", "secret");
        var window = CreateMainWindow(viewModel);

        try
        {
            window.Show();

            Assert.False(window.FindControl<Control>("LoginPageRoot")?.IsVisible);
            Assert.True(window.FindControl<Control>("SelectionPageRoot")?.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ChangePasswordWindow_ShouldLoadDialog()
    {
        var window = new ChangePasswordWindow(CreateViewModel(), "operator")
        {
            Width = 640,
            Height = 520
        };

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("OldPasswordInput")?.IsVisible);
            Assert.True(window.FindControl<Control>("ConfirmButton")?.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_ShouldExposeUpdateControls()
    {
        var window = CreateMainWindow(CreateViewModel());

        try
        {
            window.Show();

            var checkButton = window.FindControl<Control>("CheckUpdatesButton");
            var applyButton = window.FindControl<Control>("ApplyUpdateButton");
            var progressBar = window.FindControl<ProgressBar>("UpdateProgressBar");

            Assert.NotNull(checkButton);
            Assert.NotNull(applyButton);
            Assert.NotNull(progressBar);
            Assert.True(checkButton!.IsVisible);
            Assert.True(applyButton!.IsVisible);
            Assert.False(progressBar!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindow CreateMainWindow(LauncherMainViewModel viewModel)
        => new(
            viewModel,
            new LauncherLanguageService(Path.Combine(
                Path.GetTempPath(),
                $"iiot-launcher-test-language-{Guid.NewGuid():N}.json")))
        {
            Width = 1180,
            Height = 720
        };

    private static LauncherMainViewModel CreateViewModel()
        => new(
            new StubLauncherProfileCatalog(
            [
                new LauncherProfileDefinition(
                    "shell",
                    "Shell",
                    "测试工序",
                    "测试客户",
                    "default",
                    "IIoT.Edge.Shell",
                    "Shell",
                    "#000000")
            ]),
            new StubLocalAccountAuthService(LauncherAuthenticationResult.Passed(
                new LauncherAccountRecord("operator", "operator", "hash", true))),
            new StubShellLaunchService());

    private sealed class StubLauncherProfileCatalog(IReadOnlyList<LauncherProfileDefinition> profiles)
        : ILauncherProfileCatalog
    {
        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => profiles;
    }

    private sealed class StubLocalAccountAuthService(LauncherAuthenticationResult loginResult)
        : ILocalLauncherAuthService
    {
        public LauncherAuthenticationResult Authenticate(string? userName, string? password) => loginResult;

        public LauncherPasswordChangeResult ChangePassword(
            string? userName,
            string? oldPassword,
            string? newPassword)
            => LauncherPasswordChangeResult.Passed();
    }

    private sealed class StubShellLaunchService : IShellLaunchService
    {
        public bool HasRunningShellProcess => false;

        public void Launch(LauncherProfileDefinition profile)
        {
        }
    }
}
