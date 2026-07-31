using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Application.Common.Plc;
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
    public async Task OnActivatedAsync_WhenSharedSelectionMatchesRealName_ShouldExposeStablePlcCode()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("改名后的 PLC-A02");
        var viewModel = CreateViewModel(
            new FakePlcTaskBindingService(
            [
                CreateDevice(1, "PLC-A01"),
                CreateDevice(2, "改名后的 PLC-A02", "P1-AP02")
            ]),
            selectionService);

        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal("改名后的 PLC-A02", viewModel.SelectedDevice?.DeviceName);
        Assert.Equal("P1-AP02", viewModel.SelectedDevice?.PlcCode);
        Assert.Equal("改名后的 PLC-A02", selectionService.SelectedDeviceKey);
    }

    [Fact]
    public async Task OnActivatedAsync_WhenSelectedPlcWasRenamed_ShouldResolveByStablePlcCode()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("改名前", "P1-AP02")
        ]);
        selectionService.SelectDevice("改名前");
        var viewModel = CreateViewModel(
            new FakePlcTaskBindingService(
            [
                CreateDevice(2, "改名后", "P1-AP02")
            ]),
            selectionService);

        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        Assert.Equal("改名后", viewModel.SelectedDevice?.DeviceName);
        Assert.Equal("P1-AP02", viewModel.SelectedDevice?.PlcCode);
        Assert.Equal("改名前", selectionService.SelectedDeviceKey);
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

    [Fact]
    public async Task RuntimeStatusChanged_ShouldUpdateOnlyMatchingRowWithoutReloadingSqlite()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A01");
        var service = new FakePlcTaskBindingService(
        [
            CreateDevice(1, "PLC-A01", "P1-AP01"),
            CreateDevice(2, "PLC-A02", "P1-AP02")
        ]);
        var runtimeStatuses = new PlcTaskRuntimeStatusStore();
        var viewModel = CreateViewModel(
            service,
            selectionService,
            runtimeStatusReader: runtimeStatuses);
        await viewModel.OnActivatedAsync();

        runtimeStatuses.SetState(
            "p1-ap01",
            "task.upload",
            PlcTaskRuntimeState.Running);

        Assert.Equal("运行中", viewModel.Devices[0].Tasks[0].RuntimeStatusText);
        Assert.NotNull(viewModel.Devices[0].Tasks[0].LastSuccessfulAtUtc);
        Assert.Contains(
            "最近成功启动/恢复=",
            viewModel.Devices[0].Tasks[0].NoteText,
            StringComparison.Ordinal);
        Assert.Equal("等待 runtime", viewModel.Devices[1].Tasks[0].RuntimeStatusText);
        Assert.Equal(1, service.GetCalls);
        await viewModel.OnDeactivatedAsync();
    }

    [Fact]
    public async Task UnsavedEnabledDraft_ShouldNotReplacePersistedRuntimeStatus()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-A01");
        var runtimeStatuses = new PlcTaskRuntimeStatusStore();
        runtimeStatuses.SetState(
            "P1-AP01",
            "Task.Upload",
            PlcTaskRuntimeState.Running);
        var viewModel = CreateViewModel(
            new FakePlcTaskBindingService(
            [
                CreateDevice(1, "PLC-A01", "P1-AP01")
            ]),
            selectionService,
            runtimeStatusReader: runtimeStatuses);
        await viewModel.OnActivatedAsync();
        var task = Assert.Single(Assert.Single(viewModel.Devices).Tasks);
        var persistedStateTime = task.RuntimeStateChangedAtUtc;

        task.Enabled = false;

        Assert.False(task.Enabled);
        Assert.True(task.OriginalEnabled);
        Assert.Equal("运行中", task.RuntimeStatusText);
        Assert.Equal(persistedStateTime, task.RuntimeStateChangedAtUtc);
        await viewModel.OnDeactivatedAsync();
    }

    [Theory]
    [InlineData(false, true, true, "绑定缺失")]
    [InlineData(true, false, true, "已禁用")]
    [InlineData(true, true, false, "配置无效")]
    public void ConfigurationDerivedStatus_ShouldExposeItsStateTime(
        bool hasSavedBinding,
        bool enabled,
        bool canRun,
        string expectedStatus)
    {
        var configurationStateChangedAtUtc =
            DateTimeOffset.UnixEpoch.AddHours(9);
        var task = new PlcTaskBindingTaskVm(
            new PlcTaskBindingItemDto(
                "Task.Upload",
                "上传任务",
                enabled,
                hasSavedBinding,
                IsHeartbeatLike: false,
                RequiredSignals: [],
                CanRun: canRun,
                UnavailableReason: canRun ? string.Empty : "缺少 IO 信号。",
                MissingRequiredSignals: [],
                IsSupportedByCurrentPlc: true,
                ConfigurationStateChangedAtUtc: configurationStateChangedAtUtc),
            isDeviceEnabled: true);

        Assert.Equal(expectedStatus, task.RuntimeStatusText);
        Assert.Equal(
            configurationStateChangedAtUtc,
            task.RuntimeStateChangedAtUtc);
        Assert.Contains(
            "状态时间=1970-01-01 09:00:00 UTC",
            task.NoteText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnsavedEnableOfPersistedDisabledTask_ShouldRemainDisplayedAsDisabled()
    {
        var task = new PlcTaskBindingTaskVm(
            new PlcTaskBindingItemDto(
                "Task.Upload",
                "上传任务",
                Enabled: false,
                HasSavedBinding: true,
                IsHeartbeatLike: false,
                RequiredSignals: [],
                CanRun: true,
                UnavailableReason: string.Empty,
                MissingRequiredSignals: [],
                IsSupportedByCurrentPlc: true),
            isDeviceEnabled: true);

        task.Enabled = true;

        Assert.True(task.Enabled);
        Assert.False(task.OriginalEnabled);
        Assert.Equal("已禁用", task.RuntimeStatusText);
        Assert.NotNull(task.RuntimeStateChangedAtUtc);
    }

    [Fact]
    public async Task OnDeactivatedAsync_ShouldUnsubscribeRuntimeStatusChanges()
    {
        var selectionService = new DeviceSelectionService();
        var runtimeStatuses = new PlcTaskRuntimeStatusStore();
        var viewModel = CreateViewModel(
            new FakePlcTaskBindingService([CreateDevice(1, "PLC-A01", "P1-AP01")]),
            selectionService,
            runtimeStatusReader: runtimeStatuses);
        await viewModel.OnActivatedAsync();
        await viewModel.OnDeactivatedAsync();

        runtimeStatuses.SetState(
            "P1-AP01",
            "Task.Upload",
            PlcTaskRuntimeState.Faulted,
            PlcTaskRuntimeErrorCodes.TaskFault,
            nameof(InvalidOperationException));

        var task = Assert.Single(Assert.Single(viewModel.Devices).Tasks);
        Assert.Null(task.RuntimeState);
        Assert.Equal("等待 runtime", task.RuntimeStatusText);
    }

    [Fact]
    public async Task OnDeactivatedAsync_ShouldInvalidateAlreadyQueuedRuntimeStatusUpdate()
    {
        var selectionService = new DeviceSelectionService();
        var runtimeStatuses = new PlcTaskRuntimeStatusStore();
        var viewModel = CreateViewModel(
            new FakePlcTaskBindingService(
            [
                CreateDevice(1, "PLC-A01", "P1-AP01")
            ]),
            selectionService,
            runtimeStatusReader: runtimeStatuses);
        await viewModel.OnActivatedAsync();
        viewModel.QueueUiActions();

        runtimeStatuses.SetState(
            "P1-AP01",
            "Task.Upload",
            PlcTaskRuntimeState.Faulted,
            PlcTaskRuntimeErrorCodes.TaskFault,
            nameof(InvalidOperationException));
        Assert.Equal(1, viewModel.QueuedUiActionCount);

        await viewModel.OnDeactivatedAsync();
        viewModel.FlushQueuedUiActions();

        var task = Assert.Single(Assert.Single(viewModel.Devices).Tasks);
        Assert.Null(task.RuntimeState);
        Assert.Equal("等待 runtime", task.RuntimeStatusText);
    }

    [Fact]
    public async Task RuntimeFault_ShouldExposeOnlyStableCodeAndExceptionType()
    {
        var selectionService = new DeviceSelectionService();
        var runtimeStatuses = new PlcTaskRuntimeStatusStore();
        var viewModel = CreateViewModel(
            new FakePlcTaskBindingService([CreateDevice(1, "PLC-A01", "P1-AP01")]),
            selectionService,
            runtimeStatusReader: runtimeStatuses);
        await viewModel.OnActivatedAsync();

        runtimeStatuses.SetState(
            "P1-AP01",
            "Task.Upload",
            PlcTaskRuntimeState.Running);
        var lastSuccessfulAtUtc = runtimeStatuses
            .GetSnapshot("P1-AP01", "Task.Upload")!
            .LastSuccessfulAtUtc;
        runtimeStatuses.SetState(
            "P1-AP01",
            "Task.Upload",
            PlcTaskRuntimeState.Faulted,
            PlcTaskRuntimeErrorCodes.TaskFault,
            nameof(InvalidOperationException));

        var task = Assert.Single(Assert.Single(viewModel.Devices).Tasks);
        Assert.Equal("故障", task.RuntimeStatusText);
        Assert.Equal(lastSuccessfulAtUtc, task.LastSuccessfulAtUtc);
        Assert.Contains("运行错误码=TaskFault", task.NoteText, StringComparison.Ordinal);
        Assert.Contains("异常类型=InvalidOperationException", task.NoteText, StringComparison.Ordinal);
        Assert.Contains("最近成功启动/恢复=", task.NoteText, StringComparison.Ordinal);
        await viewModel.OnDeactivatedAsync();
    }

    private static TestPlcTaskBindingViewModel CreateViewModel(
        IPlcTaskBindingService service,
        IDeviceSelectionService selectionService,
        IPlcTaskBindingTransactionService? transactionService = null,
        IPlcTaskRuntimeStatusReader? runtimeStatusReader = null)
        => new TestPlcTaskBindingViewModel(
            service,
            transactionService ?? new FakePlcTaskBindingTransactionService(),
            new FakeClientPermissionService(),
            new FakeConfirmationService(),
            new TestAppLanguageService(),
            selectionService,
            runtimeStatusReader ?? new PlcTaskRuntimeStatusStore(),
            "Test.PlcTaskBinding",
            "Navigation_Title_PlcTaskBinding",
            "任务绑定",
            "TestPlugin");

    private static PlcTaskBindingDeviceDto CreateDevice(
        int id,
        string deviceName,
        string? plcCode = null)
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
                ])
            {
                PlcCode = plcCode ?? deviceName
            };

    private sealed class TestPlcTaskBindingViewModel(
        IPlcTaskBindingService bindingService,
        IPlcTaskBindingTransactionService transactionService,
        IClientPermissionService permissionService,
        IPlcTaskBindingConfirmationService confirmationService,
        TestAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService,
        IPlcTaskRuntimeStatusReader runtimeStatusReader,
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
            runtimeStatusReader,
            viewId,
            titleResourceKey,
            titleFallback,
            moduleId)
    {
        private readonly Queue<Action> _queuedUiActions = new();
        private bool _queueUiActions;

        public int QueuedUiActionCount => _queuedUiActions.Count;

        public void QueueUiActions()
            => _queueUiActions = true;

        public void FlushQueuedUiActions()
        {
            _queueUiActions = false;
            while (_queuedUiActions.TryDequeue(out var action))
            {
                action();
            }
        }

        protected override void RunOnUiThread(Action action)
        {
            if (_queueUiActions)
            {
                _queuedUiActions.Enqueue(action);
                return;
            }

            action();
        }
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

        public Task<IReadOnlySet<string>> GetConfiguredEnabledTaskKeysAsync(
            int networkDeviceId,
            IReadOnlyCollection<TaskCandidate> candidates,
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
