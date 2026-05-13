using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IIoT.Edge.Launcher;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class MainWindowBehaviorTests
{
    [Fact]
    public void MainWindow_ShouldNotHardcodeProcessChipsInHero()
    {
        var xaml = File.ReadAllText(ResolveLauncherXamlPath());

        Assert.DoesNotContain("Text=\"叠片\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"匀浆\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public Task MainWindow_ShouldInitializeComponentSuccessfully()
        => RunOnStaThreadAsync(() =>
        {
            var viewModel = new LauncherMainViewModel(
                new StubLauncherProfileCatalog([]),
                new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场管理员")),
                new StubShellLaunchService());

            var window = new MainWindow(viewModel);

            Assert.NotNull(window);
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task MainWindow_ShouldNotExposeInlineNewPasswordInput()
        => RunOnStaThreadAsync(() =>
        {
            var viewModel = new LauncherMainViewModel(
                new StubLauncherProfileCatalog([]),
                new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场管理员")),
                new StubShellLaunchService());

            var window = new MainWindow(viewModel);

            Assert.Null(window.FindName("NewPasswordInput"));
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task ChangePasswordWindow_ShouldInitializeMaskedPasswordFields()
        => RunOnStaThreadAsync(() =>
        {
            var viewModel = new LauncherMainViewModel(
                new StubLauncherProfileCatalog([]),
                new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场管理员")),
                new StubShellLaunchService());

            var window = new ChangePasswordWindow(viewModel, "admin");

            Assert.IsType<TextBox>(window.FindName("UserNameInput"));
            Assert.IsType<PasswordBox>(window.FindName("OldPasswordInput"));
            Assert.IsType<PasswordBox>(window.FindName("NewPasswordInput"));
            Assert.IsType<PasswordBox>(window.FindName("ConfirmPasswordInput"));
            window.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task ChangePasswordWindow_WhenConfirmPasswordDoesNotMatch_ShouldNotCallAuthService()
        => RunOnStaThreadAsync(() =>
        {
            var authService = new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场管理员"));
            var viewModel = new LauncherMainViewModel(
                new StubLauncherProfileCatalog([]),
                authService,
                new StubShellLaunchService());
            var window = new ChangePasswordWindow(viewModel, "admin");

            ((TextBox)window.FindName("UserNameInput")).Text = "admin";
            ((PasswordBox)window.FindName("OldPasswordInput")).Password = "123456";
            ((PasswordBox)window.FindName("NewPasswordInput")).Password = "654321";
            ((PasswordBox)window.FindName("ConfirmPasswordInput")).Password = "654322";
            ((Button)window.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(0, authService.ChangePasswordCallCount);
            window.Close();
            return Task.CompletedTask;
        });

    private static Task RunOnStaThreadAsync(Func<Task> testBody)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await testBody();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return completion.Task;
    }

    private static string ResolveLauncherXamlPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Edge",
                "IIoT.Edge.Launcher",
                "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("未找到 Launcher MainWindow.xaml。");
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

        public int ChangePasswordCallCount { get; private set; }

        public LauncherPasswordChangeResult ChangePassword(string? userName, string? oldPassword, string? newPassword)
        {
            ChangePasswordCallCount++;
            return LauncherPasswordChangeResult.Passed();
        }
    }

    private sealed class StubShellLaunchService : IShellLaunchService
    {
        public void Launch(LauncherProfileDefinition profile)
        {
        }
    }
}
