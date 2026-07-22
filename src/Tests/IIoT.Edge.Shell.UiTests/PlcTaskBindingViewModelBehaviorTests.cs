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
    public async Task SaveCommand_WhenDeviceSelected_ShouldPersistOnlySelectedDevice()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A02");
        var service = new FakePlcTaskBindingService(
        [
            CreateDevice(1, "PLC-A01"),
            CreateDevice(2, "PLC-A02")
        ]);
        var runtimeApplyService = new FakePlcRuntimeApplyService();
        var viewModel = CreateViewModel(service, selectionService, runtimeApplyService);
        await viewModel.OnActivatedAsync();

        Assert.True(viewModel.SaveCommand.CanExecute(null));
        viewModel.SaveCommand.Execute(null);
        await service.WaitForSaveAsync();
        await runtimeApplyService.WaitForApplyAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal(2, service.LastSavedNetworkDeviceId);
        Assert.Equal(2, runtimeApplyService.LastAppliedNetworkDeviceId);
        Assert.Single(service.SaveCalls);
    }

    private static PlcTaskBindingViewModel CreateViewModel(
        IPlcTaskBindingService service,
        IDeviceSelectionService selectionService,
        IPlcRuntimeApplyService? runtimeApplyService = null)
        => new TestPlcTaskBindingViewModel(
            service,
            runtimeApplyService ?? new FakePlcRuntimeApplyService(),
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
        IPlcRuntimeApplyService runtimeApplyService,
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
            runtimeApplyService,
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
        private readonly TaskCompletionSource _saveCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(int NetworkDeviceId, IReadOnlyDictionary<string, bool> States)> SaveCalls { get; } = [];

        public int? LastSavedNetworkDeviceId => SaveCalls.LastOrDefault().NetworkDeviceId;

        public Task<IReadOnlyList<PlcTaskBindingDeviceDto>> GetModuleDeviceBindingsAsync(
            string moduleId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(devices);

        public Task<IReadOnlySet<string>> GetEnabledTaskKeysAsync(
            int networkDeviceId,
            IReadOnlyCollection<TaskCandidate> candidates,
            IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
            string? deviceModel = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public Task SaveDeviceBindingsAsync(
            int networkDeviceId,
            string moduleId,
            IReadOnlyDictionary<string, bool> taskStates,
            CancellationToken cancellationToken = default)
        {
            SaveCalls.Add((networkDeviceId, taskStates));
            _saveCompletion.TrySetResult();
            return Task.CompletedTask;
        }

        public PlcTaskBindingValidationResult ValidateEnabledTasks(
            IReadOnlyCollection<TaskCandidate> candidates,
            IReadOnlySet<string> enabledTaskKeys,
            IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
            string? deviceModel = null)
            => PlcTaskBindingValidationResult.Success();

        public Task WaitForSaveAsync() => _saveCompletion.Task;
    }

    private sealed class FakePlcRuntimeApplyService : IPlcRuntimeApplyService
    {
        private readonly TaskCompletionSource _applyCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int? LastAppliedNetworkDeviceId { get; private set; }

        public string? LastAppliedDeviceName { get; private set; }

        public Task ApplyDeviceRuntimeAsync(
            int networkDeviceId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            LastAppliedNetworkDeviceId = networkDeviceId;
            _applyCompletion.TrySetResult();
            return Task.CompletedTask;
        }

        public Task ApplyDeviceRuntimeAsync(
            string deviceName,
            string reason,
            CancellationToken cancellationToken = default)
        {
            LastAppliedDeviceName = deviceName;
            _applyCompletion.TrySetResult();
            return Task.CompletedTask;
        }

        public Task WaitForApplyAsync() => _applyCompletion.Task;
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
