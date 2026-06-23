using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IIoT.Edge.Application.Abstractions.Updates;
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
    public async Task MainWindow_WhenAuthenticated_ShouldExposeDedicatedUpdateCenter()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoginAsync("operator", "secret");
        var window = CreateMainWindow(viewModel);

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("UpdateCenterPanelRoot")?.IsVisible);
            Assert.NotNull(window.FindControl<Control>("RefreshUpdateCenterButton"));
            Assert.Null(window.FindControl<Control>("VersionHistoryButton"));
            Assert.NotNull(window.FindControl<Control>("UpdateCenterRowsGrid"));
            Assert.Null(window.FindControl<Control>("ClientReleasePanelRoot"));
            Assert.Null(window.FindControl<ProgressBar>("ClientReleaseProgressBar"));
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
    public void VersionHistoryWindow_ShouldLoadDialog()
    {
        var viewModel = CreateViewModel();
        var component = new LauncherVersionComponentItem(
            EdgeComponentKind.Host,
            "Host",
            "Edge Host",
            "1.0.0",
            "宿主",
            "查看版本",
            []);
        var window = new VersionHistoryWindow(component, viewModel.ClientReleasePanel)
        {
            Width = 860,
            Height = 540
        };

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("VersionHistoryWindowRoot")?.IsVisible);
            Assert.NotNull(window.FindControl<Control>("VersionHistoryRowsGrid"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ReleaseNotesWindow_ShouldLoadDialog()
    {
        var detail = LauncherReleaseNotesDetailViewModel.FromVersionOption(
            new LauncherVersionOptionItem(
                EdgeComponentKind.Plugin,
                "Homogenization",
                "均浆",
                "1.0.0",
                "1.1.0",
                EdgeVersionStatus.Newer,
                canApply: true,
                compatibilityIssue: string.Empty,
                packageSizeText: "101.0 KB",
                publishedAtUtc: new DateTime(2026, 6, 22, 16, 15, 54, DateTimeKind.Utc),
                releaseNotes: "客户端更新：设备安装状态上报本机 IP/远端 IP、宿主和插件版本。",
                statusKind: "Warning",
                statusText: "可更新",
                actionKind: "Secondary",
                actionText: "更新"),
            "工序插件");
        var window = new ReleaseNotesWindow(detail)
        {
            Width = 640,
            Height = 520
        };

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("ReleaseNotesWindowRoot")?.IsVisible);
            Assert.Equal(
                detail.ReleaseNotesText,
                window.FindControl<TextBlock>("ReleaseNotesTextBlock")?.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_ShouldNotExposeSeparateUpdateControls()
    {
        var window = CreateMainWindow(CreateViewModel());

        try
        {
            window.Show();

            Assert.Null(window.FindControl<Control>("CheckUpdatesButton"));
            Assert.Null(window.FindControl<Control>("ApplyUpdateButton"));
            Assert.Null(window.FindControl<ProgressBar>("UpdateProgressBar"));
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
