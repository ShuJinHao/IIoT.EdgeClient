using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class PlcTaskBindingViewModelBehaviorTests
{
    [Fact]
    public async Task OnActivatedAsync_WhenSharedSelectionIsAll_ShouldNotAutoSelectFirstDevice()
    {
        var selectionService = new DeviceSelectionService();
        var service = new FakePlcTaskBindingService(
        [
            CreateDevice(1, "PLC-A01"),
            CreateDevice(2, "PLC-A02")
        ]);
        var viewModel = CreateViewModel(service, selectionService);

        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal(2, viewModel.Devices.Count);
        Assert.Null(viewModel.SelectedDevice);
        Assert.False(viewModel.CanSave);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.Equal(IDeviceSelectionService.AllFilterKey, selectionService.SelectedDeviceKey);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenSharedSelectionMatchesDeviceName_ShouldSelectThatDevice()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A02");
        var viewModel = CreateViewModel(
            new FakePlcTaskBindingService(
            [
                CreateDevice(1, "PLC-A01"),
                CreateDevice(2, "PLC-A02")
            ]),
            selectionService);

        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal("PLC-A02", viewModel.SelectedDevice?.DeviceName);
        Assert.Equal("PLC-A02", selectionService.SelectedDeviceKey);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenDeviceSelected_ShouldExposeCurrentDeviceTextWithoutSelectPrompt()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A02");
        var viewModel = CreateViewModel(
            new FakePlcTaskBindingService(
            [
                CreateDevice(1, "PLC-A01"),
                CreateDevice(2, "PLC-A02")
            ]),
            selectionService);

        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal("当前 PLC：PLC-A02", viewModel.SelectedDeviceTitle);
        Assert.DoesNotContain("选择设备", viewModel.SelectedDeviceTitle);
        Assert.DoesNotContain("选择设备", viewModel.SelectedDevice?.DeviceStateText ?? string.Empty);
    }

    [Fact]
    public async Task SelectedDevice_WhenSetInsideBindingPage_ShouldNotWriteSharedSelection()
    {
        var selectionService = new DeviceSelectionService();
        var viewModel = CreateViewModel(
            new FakePlcTaskBindingService(
            [
                CreateDevice(1, "PLC-A01"),
                CreateDevice(2, "PLC-A02")
            ]),
            selectionService);
        await viewModel.OnActivatedAsync();

        viewModel.SelectedDevice = viewModel.Devices.Single(device => device.DeviceName == "PLC-A01");
        await viewModel.OnDeactivatedAsync();

        Assert.Equal(IDeviceSelectionService.AllFilterKey, selectionService.SelectedDeviceKey);
        Assert.Equal("PLC-A01", viewModel.SelectedDevice?.DeviceName);
    }

    [Fact]
    public async Task SaveCommand_WhenDeviceSelected_ShouldUseOneTransactionForOnlySelectedDevice()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A02");
        var service = new FakePlcTaskBindingService(
        [
            CreateDevice(1, "PLC-A01"),
            CreateDevice(2, "PLC-A02")
        ]);
        var transactionService = new FakePlcTaskBindingTransactionService();
        var viewModel = CreateViewModel(service, selectionService, transactionService);
        await viewModel.OnActivatedAsync();

        Assert.True(viewModel.SaveCommand.CanExecute(null));
        viewModel.SaveCommand.Execute(null);
        await transactionService.WaitForSaveAndApplyAsync();
        await service.WaitForReloadAfterSaveAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal(2, transactionService.LastNetworkDeviceId);
        Assert.Equal("TestPlugin", transactionService.LastModuleId);
        Assert.Equal("任务绑定已保存并已应用到当前 PLC。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveCommand_WhenPlcIsOffline_ShouldTreatWaitingAsSuccessfulSave()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A01");
        var service = new FakePlcTaskBindingService([CreateDevice(1, "PLC-A01")]);
        var transactionService = new FakePlcTaskBindingTransactionService
        {
            ResultState = PlcTaskBindingSaveApplyState.WaitingForConnection
        };
        var viewModel = CreateViewModel(service, selectionService, transactionService);
        await viewModel.OnActivatedAsync();

        viewModel.SaveCommand.Execute(null);
        await transactionService.WaitForSaveAndApplyAsync();
        await service.WaitForReloadAfterSaveAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal("任务绑定已保存，等待 PLC。", viewModel.StatusMessage);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task SaveCommand_WhenTransactionFails_ShouldReloadDatabaseTruthAndShowNoSuccess()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A01");
        var service = new FakePlcTaskBindingService([CreateDevice(1, "PLC-A01")]);
        var transactionService = new FakePlcTaskBindingTransactionService
        {
            Failure = new InvalidOperationException("runtime apply failed")
        };
        var viewModel = CreateViewModel(service, selectionService, transactionService);
        await viewModel.OnActivatedAsync();

        viewModel.SaveCommand.Execute(null);
        await transactionService.WaitForSaveAndApplyAsync();
        await service.WaitForReloadAfterSaveAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal(2, service.GetCalls);
        Assert.True(viewModel.HasError);
        Assert.DoesNotContain("已应用", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    private static PlcTaskBindingViewModel CreateViewModel(
        IPlcTaskBindingService service,
        IDeviceSelectionService selectionService,
        IPlcTaskBindingTransactionService? transactionService = null)
        => new TestPlcTaskBindingViewModel(
            service,
            transactionService ?? new FakePlcTaskBindingTransactionService(),
            new FakeClientPermissionService(),
            new FakeConfirmationService(),
            new TestAppLanguageService(),
            selectionService,
            "Test.PlcTaskBinding",
            "Navigation_Title_PlcTaskBinding",
            "任务绑定",
            "TestPlugin");

    private static PlcTaskBindingDeviceDto CreateDevice(int id, string deviceName)
        => new(
            id,
            deviceName,
            "TestPlugin",
            IsDeviceEnabled: true,
            Tasks:
            [
                new PlcTaskBindingItemDto(
                    "Task.Upload",
                    "上传任务",
                    Enabled: true,
                    HasSavedBinding: true,
                    IsHeartbeatLike: false,
                    RequiredSignals: [],
                    CanRun: true,
                    UnavailableReason: string.Empty,
                    MissingRequiredSignals: [],
                    IsSupportedByCurrentPlc: true)
            ]);

    private sealed class TestPlcTaskBindingViewModel(
        IPlcTaskBindingService bindingService,
        IPlcTaskBindingTransactionService transactionService,
        IClientPermissionService permissionService,
        IPlcTaskBindingConfirmationService confirmationService,
        TestAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService,
        string viewId,
        string titleResourceKey,
        string titleFallback,
        string moduleId)
        : PlcTaskBindingViewModel(
            bindingService,
            transactionService,
            permissionService,
            confirmationService,
            languageService,
            deviceSelectionService,
            viewId,
            titleResourceKey,
            titleFallback,
            moduleId)
    {
        protected override void RunOnUiThread(Action action) => action();
    }

    private sealed class FakePlcTaskBindingService(
        IReadOnlyList<PlcTaskBindingDeviceDto> devices) : IPlcTaskBindingService
    {
        private readonly TaskCompletionSource _reloadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int GetCalls { get; private set; }

        public Task<IReadOnlyList<PlcTaskBindingDeviceDto>> GetModuleDeviceBindingsAsync(
            string moduleId,
            CancellationToken cancellationToken = default)
        {
            GetCalls++;
            if (GetCalls >= 2)
            {
                _reloadCompletion.TrySetResult();
            }

            return Task.FromResult(devices);
        }

        public Task<IReadOnlySet<string>> GetEnabledTaskKeysAsync(
            int networkDeviceId,
            IReadOnlyCollection<TaskCandidate> candidates,
            IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
            string? deviceModel = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public PlcTaskBindingValidationResult ValidateEnabledTasks(
            IReadOnlyCollection<TaskCandidate> candidates,
            IReadOnlySet<string> enabledTaskKeys,
            IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
            string? deviceModel = null)
            => PlcTaskBindingValidationResult.Success();

        public Task WaitForReloadAfterSaveAsync() => _reloadCompletion.Task;
    }

    private sealed class FakePlcTaskBindingTransactionService
        : IPlcTaskBindingTransactionService
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int? LastNetworkDeviceId { get; private set; }

        public string? LastModuleId { get; private set; }

        public PlcTaskBindingSaveApplyState ResultState { get; init; }
            = PlcTaskBindingSaveApplyState.Applied;

        public Exception? Failure { get; init; }

        public Task<PlcTaskBindingSaveApplyResult> SaveAndApplyAsync(
            int networkDeviceId,
            string moduleId,
            IReadOnlyDictionary<string, bool> taskStates,
            CancellationToken cancellationToken = default)
        {
            LastNetworkDeviceId = networkDeviceId;
            LastModuleId = moduleId;
            _completion.TrySetResult();
            return Failure is null
                ? Task.FromResult(new PlcTaskBindingSaveApplyResult(ResultState, taskStates
                    .Where(static state => state.Value)
                    .Select(static state => state.Key)
                    .ToArray()))
                : Task.FromException<PlcTaskBindingSaveApplyResult>(Failure);
        }

        public Task WaitForSaveAndApplyAsync() => _completion.Task;
    }

    private sealed class FakeClientPermissionService : IClientPermissionService
    {
        public bool CanEditParams => true;

        public bool CanEditHardware => true;

        public bool IsLocalAdmin => true;

        public event Action? PermissionStateChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => true;
    }

    private sealed class FakeConfirmationService : IPlcTaskBindingConfirmationService
    {
        public Task<bool> ConfirmDisableHeartbeatAsync(string deviceName, IReadOnlyCollection<string> taskNames)
            => Task.FromResult(true);
    }
}
