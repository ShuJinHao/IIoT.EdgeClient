using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Presentation.Shell.Features.Login;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class LoginViewModelBehaviorTests
{
    [Fact]
    public Task LoginCommand_WhenDeviceIdentityIsNotReady_ShouldRejectCloudLoginAndClearPassword()
        => RunOnStaThreadAsync(async () =>
        {
            var authService = new FakeAuthService();
            var viewModel = new LoginViewModel(authService, new FakeDeviceService())
            {
                EmployeeNo = "E001",
                Password = "pwd123"
            };

            viewModel.LoginCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsBusy);

            Assert.Equal("Device cloud identity is not ready yet.", viewModel.ErrorMessage);
            Assert.Equal(string.Empty, viewModel.Password);
            Assert.Equal(0, authService.LoginCloudCallCount);
        });

    [Fact]
    public Task LoginCommand_WhenCloudLoginIsRunning_ShouldSetBusyAndClearPasswordAfterFailure()
        => RunOnStaThreadAsync(async () =>
        {
            var loginTask = new TaskCompletionSource<AuthResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var authService = new FakeAuthService
            {
                LoginCloudHandler = (_, _, _) => loginTask.Task
            };
            var deviceService = new FakeDeviceService
            {
                CurrentDevice = new DeviceSession
                {
                    DeviceId = Guid.NewGuid(),
                    DeviceName = "Device-A",
                    ClientCode = "LINE-01",
                    ProcessId = Guid.NewGuid(),
                    UploadAccessToken = "device-token",
                    UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
                }
            };
            var viewModel = new LoginViewModel(authService, deviceService)
            {
                EmployeeNo = "  E1001  ",
                Password = "  secret  "
            };

            viewModel.LoginCommand.Execute(null);
            await WaitUntilAsync(() => viewModel.IsBusy);

            Assert.True(viewModel.IsBusy);
            Assert.Equal(1, authService.LoginCloudCallCount);

            loginTask.SetResult(AuthResult.Fail("Invalid employee number or password."));
            await WaitUntilAsync(() => !viewModel.IsBusy);

            Assert.False(viewModel.IsBusy);
            Assert.Equal(string.Empty, viewModel.Password);
            Assert.Equal("E1001", viewModel.EmployeeNo);
            Assert.Equal("Invalid employee number or password.", viewModel.ErrorMessage);
        });

    [Fact]
    public Task LoginCommand_WhenCloudLoginSucceeds_ShouldRaiseSuccessAndClearInputs()
        => RunOnStaThreadAsync(async () =>
        {
            var authService = new FakeAuthService
            {
                LoginCloudHandler = (_, _, _) => Task.FromResult(AuthResult.Ok("Welcome"))
            };
            var deviceService = new FakeDeviceService
            {
                CurrentDevice = new DeviceSession
                {
                    DeviceId = Guid.NewGuid(),
                    DeviceName = "Device-A",
                    ClientCode = "LINE-01",
                    ProcessId = Guid.NewGuid(),
                    UploadAccessToken = "device-token",
                    UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
                }
            };
            var viewModel = new LoginViewModel(authService, deviceService)
            {
                EmployeeNo = "E9001",
                Password = "secret"
            };
            var loginSucceeded = false;
            viewModel.LoginSucceeded += () => loginSucceeded = true;

            viewModel.LoginCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsBusy && loginSucceeded);

            Assert.True(loginSucceeded);
            Assert.Equal(string.Empty, viewModel.EmployeeNo);
            Assert.Equal(string.Empty, viewModel.Password);
            Assert.Equal(string.Empty, viewModel.ErrorMessage);
        });

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate(), "Condition was not satisfied before timeout.");
    }

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

    private sealed class FakeAuthService : IAuthService
    {
        public Func<string, Task<AuthResult>>? LoginLocalHandler { get; init; }
        public Func<string, string, Guid, Task<AuthResult>>? LoginCloudHandler { get; init; }

        public UserSession? CurrentUser => null;
        public bool IsAuthenticated => false;
        public int LoginCloudCallCount { get; private set; }
        public event Action<UserSession?>? AuthStateChanged;

        public bool HasPermission(string permission) => false;

        public Task<AuthResult> LoginLocalAsync(string password)
            => LoginLocalHandler?.Invoke(password) ?? Task.FromResult(AuthResult.Fail("Not configured"));

        public Task<AuthResult> LoginCloudAsync(string employeeNo, string password, Guid deviceId)
        {
            LoginCloudCallCount++;
            return LoginCloudHandler?.Invoke(employeeNo, password, deviceId)
                ?? Task.FromResult(AuthResult.Fail("Login failed"));
        }

        public void Logout()
        {
            AuthStateChanged?.Invoke(null);
        }
    }

    private sealed class FakeDeviceService : IDeviceService
    {
        public DeviceSession? CurrentDevice { get; set; }
        public NetworkState CurrentState => CurrentDevice is null ? NetworkState.Offline : NetworkState.Online;
        public EdgeUploadGateSnapshot CurrentUploadGate => CurrentDevice is null
            ? new EdgeUploadGateSnapshot
            {
                State = EdgeUploadGateState.Blocked,
                Reason = EdgeUploadBlockReason.DeviceUnidentified
            }
            : new EdgeUploadGateSnapshot
            {
                State = EdgeUploadGateState.Ready,
                Reason = EdgeUploadBlockReason.None,
                TokenExpiresAtUtc = CurrentDevice.UploadAccessTokenExpiresAtUtc
            };
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
        }
    }
}
