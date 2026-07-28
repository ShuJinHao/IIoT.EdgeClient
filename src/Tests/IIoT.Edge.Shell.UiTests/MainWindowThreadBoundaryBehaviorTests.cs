using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Shell.ViewModels;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Shell.UiTests;

public sealed class MainWindowThreadBoundaryBehaviorTests
{
    [AvaloniaFact]
    public async Task BackgroundLoginFailureRefreshLogoutDeviceAndLanguageEvents_ShouldKeepBindingsOnUiThread()
    {
        var language = new TestAppLanguageService();
        var auth = new TestAuthService();
        var device = new TestDeviceService();
        using var viewModel = new MainWindowViewModel(
            language,
            new ConfigurationBuilder().Build(),
            auth,
            device);
        var observed = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        var offUiNotifications = new ConcurrentQueue<string>();
        var notificationCount = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.PropertyName))
            {
                return;
            }

            if (!Dispatcher.UIThread.CheckAccess())
            {
                offUiNotifications.Enqueue(args.PropertyName);
            }

            observed[args.PropertyName] = true;
            Interlocked.Increment(ref notificationCount);
        };

        await Task.Run(
            () =>
            {
                auth.Publish(new UserSession
                {
                    DisplayName = "云端员工",
                    EmployeeNo = "E1001",
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
                });
                device.Publish(new DeviceSession
                {
                    DeviceId = Guid.NewGuid(),
                    DeviceName = "正极模切客户端"
                });
                language.Change(CultureInfo.GetCultureInfo("en-US"));
            },
            TestContext.Current.CancellationToken);

        await WaitForPropertyStateAsync(viewModel, () =>
            observed.ContainsKey(nameof(MainWindowViewModel.OperatorName))
            && observed.ContainsKey(nameof(MainWindowViewModel.HasCloudDeviceIdentity))
            && observed.ContainsKey(nameof(MainWindowViewModel.AppTitle)));

        Assert.Empty(offUiNotifications);
        Assert.Equal("云端员工", viewModel.OperatorName);
        Assert.True(viewModel.HasCloudDeviceIdentity);

        await Dispatcher.UIThread.InvokeAsync(static () => { });
        var beforeFailedLogin = Volatile.Read(ref notificationCount);
        var failedLogin = await Task.Run(
            () => auth.LoginCloudAsync("E1001", "wrong", Guid.NewGuid()),
            TestContext.Current.CancellationToken);
        Assert.False(failedLogin.Success);
        await Dispatcher.UIThread.InvokeAsync(static () => { });
        Assert.Equal(beforeFailedLogin, Volatile.Read(ref notificationCount));

        var beforeRefresh = Volatile.Read(ref notificationCount);
        await Task.Run(
            () => auth.Publish(new UserSession
            {
                DisplayName = "云端员工（已刷新）",
                EmployeeNo = "E1001",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(20)
            }),
            TestContext.Current.CancellationToken);
        await WaitForPropertyStateAsync(viewModel, () =>
            Volatile.Read(ref notificationCount) > beforeRefresh
            && viewModel.OperatorName == "云端员工（已刷新）");

        var beforeRefreshFailure = Volatile.Read(ref notificationCount);
        await Task.Run(
            () => auth.Publish(null),
            TestContext.Current.CancellationToken);
        await WaitForPropertyStateAsync(viewModel, () =>
            Volatile.Read(ref notificationCount) > beforeRefreshFailure
            && !viewModel.IsAuthenticated);

        await Task.Run(
            () => auth.Publish(new UserSession
            {
                DisplayName = "云端员工（重新登录）",
                EmployeeNo = "E1001",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            }),
            TestContext.Current.CancellationToken);
        await WaitForPropertyStateAsync(viewModel, () => viewModel.IsAuthenticated);

        var beforeLogout = Volatile.Read(ref notificationCount);
        await Task.Run(auth.Logout, TestContext.Current.CancellationToken);
        await WaitForPropertyStateAsync(viewModel, () =>
            Volatile.Read(ref notificationCount) > beforeLogout
            && !viewModel.IsAuthenticated);

        Assert.Empty(offUiNotifications);
    }

    [Fact]
    public void DispatcherExceptionPolicy_ShouldKeepRuntimeShellButTreatStartupAsFatal()
    {
        Assert.Equal(
            ShellDispatcherExceptionDisposition.FatalStartup,
            ShellDispatcherExceptionPolicy.Resolve(mainWindowReady: false));
        Assert.Equal(
            ShellDispatcherExceptionDisposition.RecoverRuntime,
            ShellDispatcherExceptionPolicy.Resolve(mainWindowReady: true));
    }

    private static async Task WaitForPropertyStateAsync(
        MainWindowViewModel viewModel,
        Func<bool> condition)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler handler = (_, _) =>
        {
            if (condition())
            {
                completion.TrySetResult(true);
            }
        };
        viewModel.PropertyChanged += handler;
        try
        {
            if (condition())
            {
                return;
            }

            await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
        }
    }

    private sealed class TestAuthService : IAuthService
    {
        public UserSession? CurrentUser { get; private set; }

        public bool IsAuthenticated => CurrentUser is not null;

        public LocalAdminCredentialStatus LocalAdminCredentialStatus => LocalAdminCredentialStatus.Ready;

        public event Action<UserSession?>? AuthStateChanged;

        public bool HasPermission(string permission) => false;

        public Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(IsAuthenticated);

        public Task<AuthResult> LoginLocalAsync(string password)
            => Task.FromResult(AuthResult.Fail("not used"));

        public Task<AuthResult> InitializeLocalAdminAsync(string newPassword)
            => Task.FromResult(AuthResult.Fail("not used"));

        public Task<AuthResult> ResetLocalAdminPasswordAsync(string currentPassword, string newPassword)
            => Task.FromResult(AuthResult.Fail("not used"));

        public Task<AuthResult> LoginCloudAsync(string employeeNo, string password, Guid deviceId)
            => Task.FromResult(AuthResult.Fail("not used"));

        public void Logout() => Publish(null);

        public void Publish(UserSession? session)
        {
            CurrentUser = session;
            AuthStateChanged?.Invoke(session);
        }
    }

    private sealed class TestDeviceService : IDeviceService
    {
        public DeviceSession? CurrentDevice { get; private set; }

        public NetworkState CurrentState { get; private set; } = NetworkState.Offline;

        public EdgeUploadGateSnapshot CurrentUploadGate { get; private set; } = new();

        public bool HasDeviceId => CurrentDevice?.DeviceId != Guid.Empty;

        public bool CanUploadToCloud => CurrentUploadGate.State == EdgeUploadGateState.Ready;

        public event Action<NetworkState>? NetworkStateChanged;

        public event Action<DeviceSession?>? DeviceIdentified;

        public event Action<EdgeUploadGateSnapshot>? UploadGateChanged;

        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public Task RefreshBootstrapAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void MarkUploadGateBlocked(EdgeUploadBlockReason reason, DateTimeOffset occurredAtUtc)
        {
            CurrentUploadGate = new EdgeUploadGateSnapshot
            {
                State = EdgeUploadGateState.Blocked,
                Reason = reason
            };
            UploadGateChanged?.Invoke(CurrentUploadGate);
        }

        public void Publish(DeviceSession session)
        {
            CurrentDevice = session;
            CurrentState = NetworkState.Online;
            CurrentUploadGate = new EdgeUploadGateSnapshot
            {
                State = EdgeUploadGateState.Ready,
                Reason = EdgeUploadBlockReason.None
            };
            DeviceIdentified?.Invoke(session);
            NetworkStateChanged?.Invoke(CurrentState);
            UploadGateChanged?.Invoke(CurrentUploadGate);
        }
    }
}
