using System.Threading;
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
}
