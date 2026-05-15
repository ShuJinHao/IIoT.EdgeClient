using Avalonia.Headless.XUnit;
using IIoT.Edge.Launcher.Avalonia.Views;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using System.Diagnostics;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class LauncherAvaloniaBehaviorTests
{
    [AvaloniaFact]
    public async Task Launcher_window_can_bind_login_and_profile_selection()
    {
        var starter = new SpyProcessStarter();
        var executablePath = CreateTempExecutable();
        try
        {
            var profile = new LauncherProfileDefinition(
                "HomogenizationLineAvalonia",
                "匀浆 Avalonia",
                "启动 Avalonia 迁移客户端",
                null,
                "HomogenizationLine",
                executablePath,
                "BeakerOutline",
                "#2563EB");
            var viewModel = new LauncherMainViewModel(
                new StubLauncherProfileCatalog([profile]),
                new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场启动管理员")),
                new ShellLaunchService(starter));

            var window = new MainWindow(viewModel);
            await viewModel.LoginAsync("101650", "123456");
            await viewModel.LaunchAsync(profile);

            Assert.NotNull(window);
            Assert.True(viewModel.IsAuthenticated);
            Assert.Single(viewModel.Profiles);
            Assert.NotNull(starter.StartInfo);
            Assert.Equal(executablePath, starter.StartInfo!.FileName);
            Assert.Equal("HomogenizationLine", starter.StartInfo.EnvironmentVariables["Shell__MachineProfile"]);
            window.Close();
        }
        finally
        {
            DeleteTempExecutable(executablePath);
        }
    }

    [AvaloniaFact]
    public void Change_password_window_can_be_created_with_launcher_view_model()
    {
        var viewModel = new LauncherMainViewModel(
            new StubLauncherProfileCatalog([]),
            new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场启动管理员")),
            new StubShellLaunchService());

        var window = new ChangePasswordWindow(viewModel, "101650");

        Assert.NotNull(window);
        window.Close();
    }

    private static string CreateTempExecutable()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-avalonia-tests",
            Guid.NewGuid().ToString("N"),
            "IIoT.Edge.AvaloniaShell.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static void DeleteTempExecutable(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
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

        public LauncherPasswordChangeResult ChangePassword(string? userName, string? oldPassword, string? newPassword)
            => LauncherPasswordChangeResult.Passed();
    }

    private sealed class StubShellLaunchService : IShellLaunchService
    {
        public void Launch(LauncherProfileDefinition profile)
        {
        }
    }

    private sealed class SpyProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return Process.GetCurrentProcess();
        }
    }
}
