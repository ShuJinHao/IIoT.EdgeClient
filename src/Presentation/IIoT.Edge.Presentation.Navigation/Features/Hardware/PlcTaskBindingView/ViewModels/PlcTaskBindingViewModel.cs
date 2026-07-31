using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows.Input;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Presentation.Navigation.Common;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;

public class PlcTaskBindingViewModel : NavigationViewModelBase
{
    private readonly IPlcTaskBindingService _bindingService;
    private readonly IPlcTaskBindingTransactionService _bindingTransactionService;
    private readonly IClientPermissionService _permissionService;
    private readonly IPlcTaskBindingConfirmationService _confirmationService;
    private readonly IDeviceSelectionService _deviceSelectionService;
    private readonly IPlcTaskRuntimeStatusReader _runtimeStatusReader;
    private readonly string _moduleId;
    private readonly AsyncCommand _saveCommand;
    private PlcTaskBindingDeviceVm? _selectedDevice;
    private bool _isDeviceSelectionSubscribed;
    private int _runtimeStatusSubscriptionActive;
    private int _runtimeStatusSubscriptionGeneration;

    public PlcTaskBindingViewModel(
        IPlcTaskBindingService bindingService,
        IPlcTaskBindingTransactionService bindingTransactionService,
        IClientPermissionService permissionService,
        IPlcTaskBindingConfirmationService confirmationService,
        IAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService,
        IPlcTaskRuntimeStatusReader runtimeStatusReader)
        : this(
            bindingService,
            bindingTransactionService,
            permissionService,
            confirmationService,
            languageService,
            deviceSelectionService,
            runtimeStatusReader,
            "Hardware.PlcTaskBindingView",
            "Navigation_Title_PlcTaskBinding",
            "任务绑定",
            moduleId: string.Empty)
    {
    }

    public PlcTaskBindingViewModel(
        IPlcTaskBindingService bindingService,
        IPlcTaskBindingTransactionService bindingTransactionService,
        IClientPermissionService permissionService,
        IPlcTaskBindingConfirmationService confirmationService,
        IAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService,
        IPlcTaskRuntimeStatusReader runtimeStatusReader,
        string viewId,
        string titleResourceKey,
        string titleFallback,
        string moduleId)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _bindingService = bindingService;
        _bindingTransactionService = bindingTransactionService;
        _permissionService = permissionService;
        _confirmationService = confirmationService;
        _deviceSelectionService = deviceSelectionService;
        _runtimeStatusReader = runtimeStatusReader;
        _moduleId = moduleId;

        RefreshCommand = new AsyncCommand(LoadAsync);
        _saveCommand = new AsyncCommand(SaveAsync, () => CanSave);
    }

    public ObservableCollection<PlcTaskBindingDeviceVm> Devices { get; } = [];

    public PlcTaskBindingDeviceVm? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value))
            {
                return;
            }

            _selectedDevice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedDevice));
            OnPropertyChanged(nameof(IsDeviceSelectionRequired));
            OnPropertyChanged(nameof(ShouldShowDeviceSelectionPrompt));
            OnPropertyChanged(nameof(SelectedDeviceDisplayName));
            OnPropertyChanged(nameof(SelectedDeviceTitle));
            OnPropertyChanged(nameof(SelectedDeviceTasks));
            OnPropertyChanged(nameof(CanSave));
            _saveCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<PlcTaskBindingTaskVm>? SelectedDeviceTasks => SelectedDevice?.Tasks;

    public bool HasDevices => Devices.Count > 0;

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool IsDeviceSelectionRequired => SelectedDevice is null;

    public bool ShouldShowDeviceSelectionPrompt => HasDevices && IsDeviceSelectionRequired;

    public string SelectedDeviceDisplayName
        => SelectedDevice is null
            ? GetText("Navigation_DeviceSelection_AllOrSummary", "全部/汇总")
            : FormatPlcIdentity(SelectedDevice.PlcCode, SelectedDevice.DeviceName);

    public string SelectedDeviceTitle
        => SelectedDevice is null
            ? GetText("Navigation_DeviceSelection_AllOrSummary", "全部/汇总")
            : FormatText(
                "Navigation_DeviceSelection_CurrentPlcFormat",
                "当前 PLC：{0}",
                FormatPlcIdentity(SelectedDevice.PlcCode, SelectedDevice.DeviceName));

    public bool CanEdit => _permissionService.CanEditHardware;

    public bool CanSave => CanEdit && SelectedDevice is not null;

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand => _saveCommand;

    public override Task OnActivatedAsync()
    {
        SubscribeDeviceSelection();
        SubscribeRuntimeStatus();
        return LoadAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        UnsubscribeDeviceSelection();
        UnsubscribeRuntimeStatus();
        return Task.CompletedTask;
    }

    private async Task LoadAsync()
        => await RunViewTaskAsync(async () =>
        {
            await LoadCoreAsync().ConfigureAwait(false);
        }, GetText("Navigation_PlcTaskBinding_LoadFailed", "加载 PLC 任务绑定失败。"));

    private async Task SaveAsync()
        => await RunViewTaskAsync(async () =>
        {
            var selected = SelectedDevice;
            if (selected is null)
            {
                SetError(GetText("Navigation_PlcTaskBinding_SelectDeviceFirst", "请先选择 PLC 设备。"));
                return;
            }

            var disabledHeartbeatTasks = selected.Tasks
                .Where(static x => x.IsHeartbeatLike && x.OriginalEnabled && !x.Enabled)
                .Select(static x => x.DisplayName)
                .ToArray();
            if (disabledHeartbeatTasks.Length > 0
                && !await _confirmationService.ConfirmDisableHeartbeatAsync(selected.DeviceName, disabledHeartbeatTasks))
            {
                SetStatus(GetText("Navigation_PlcTaskBinding_SaveCanceled", "已取消保存。"));
                return;
            }

            var states = selected.Tasks.ToDictionary(static x => x.Key, static x => x.Enabled, StringComparer.OrdinalIgnoreCase);
            PlcTaskBindingSaveApplyResult result;
            try
            {
                result = await _bindingTransactionService
                    .SaveAndApplyAsync(
                    selected.NetworkDeviceId,
                    _moduleId,
                    states)
                    .ConfigureAwait(false);
            }
            catch (Exception primaryFailure)
            {
                try
                {
                    await LoadCoreAsync().ConfigureAwait(false);
                }
                catch (Exception reloadFailure)
                {
                    throw new InvalidOperationException(
                        "PLC 任务绑定保存失败，且页面重新读取 SQLite 真值失败。",
                        new AggregateException(primaryFailure, reloadFailure));
                }

                ExceptionDispatchInfo.Capture(primaryFailure).Throw();
                throw;
            }

            await LoadCoreAsync().ConfigureAwait(false);
            RunOnUiThread(() => SetStatus(result.State == PlcTaskBindingSaveApplyState.Applied
                ? GetText("Navigation_PlcTaskBinding_SaveSuccess", "任务绑定已保存并已应用到当前 PLC。")
                : GetText("Navigation_PlcTaskBinding_SaveWaitingForPlc", "任务绑定已保存，等待 PLC。")));
        }, GetText("Navigation_PlcTaskBinding_SaveFailed", "保存 PLC 任务绑定失败。"));

    private async Task LoadCoreAsync()
    {
        var devices = await _bindingService.GetModuleDeviceBindingsAsync(_moduleId).ConfigureAwait(false);
        var deviceItems = devices.Select(static x => new PlcTaskBindingDeviceVm(x)).ToArray();

        RunOnUiThread(() =>
        {
            ReplaceItems(Devices, deviceItems);
            foreach (var device in Devices)
            {
                foreach (var task in device.Tasks)
                {
                    task.ApplyRuntimeSnapshot(
                        _runtimeStatusReader.GetSnapshot(device.PlcCode, task.Key));
                }
            }

            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(ShouldShowDeviceSelectionPrompt));

            ApplySelectedDeviceFromSharedSelection(ResolveDeviceFromSharedSelection());

            SetStatus(Devices.Count == 0
                ? GetText("Navigation_PlcTaskBinding_NoDevices", "当前模块暂无 PLC 设备。")
                : FormatText("Navigation_PlcTaskBinding_DeviceCountFormat", "已加载 {0} 台 PLC 的任务绑定。", Devices.Count));
        });
    }

    private PlcTaskBindingDeviceVm? ResolveDeviceFromSharedSelection()
    {
        var selectedKey = _deviceSelectionService.SelectedDeviceKey;
        if (string.Equals(
                selectedKey,
                IDeviceSelectionService.AllFilterKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var selectedPlcCode = _deviceSelectionService.SelectedPlcCode;
        if (!string.IsNullOrWhiteSpace(selectedPlcCode))
        {
            var byPlcCode = Devices
                .Where(device => string.Equals(
                    device.PlcCode,
                    selectedPlcCode,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            return byPlcCode.Length == 1 ? byPlcCode[0] : null;
        }

        var byDeviceName = Devices
            .Where(device => string.Equals(
                device.DeviceName,
                selectedKey,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return byDeviceName.Length == 1 ? byDeviceName[0] : null;
    }

    private static string FormatPlcIdentity(string? plcCode, string deviceName)
        => string.IsNullOrWhiteSpace(plcCode)
           || string.Equals(plcCode, deviceName, StringComparison.OrdinalIgnoreCase)
            ? deviceName
            : $"{plcCode} · {deviceName}";

    private void ApplySelectedDeviceFromSharedSelection(PlcTaskBindingDeviceVm? device)
    {
        SelectedDevice = device;
    }

    private void OnSharedDeviceSelectionChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplySelectedDeviceFromSharedSelection(ResolveDeviceFromSharedSelection());
            return;
        }

        Dispatcher.UIThread.Post(
            () => ApplySelectedDeviceFromSharedSelection(ResolveDeviceFromSharedSelection()),
            DispatcherPriority.Background);
    }

    private void SubscribeDeviceSelection()
    {
        if (_isDeviceSelectionSubscribed)
        {
            return;
        }

        _deviceSelectionService.SelectionChanged += OnSharedDeviceSelectionChanged;
        _isDeviceSelectionSubscribed = true;
    }

    private void UnsubscribeDeviceSelection()
    {
        if (!_isDeviceSelectionSubscribed)
        {
            return;
        }

        _deviceSelectionService.SelectionChanged -= OnSharedDeviceSelectionChanged;
        _isDeviceSelectionSubscribed = false;
    }

    private void SubscribeRuntimeStatus()
    {
        if (Interlocked.CompareExchange(
                ref _runtimeStatusSubscriptionActive,
                1,
                0) != 0)
        {
            return;
        }

        Interlocked.Increment(ref _runtimeStatusSubscriptionGeneration);
        _runtimeStatusReader.StatusChanged += OnRuntimeStatusChanged;
    }

    private void UnsubscribeRuntimeStatus()
    {
        if (Interlocked.Exchange(
                ref _runtimeStatusSubscriptionActive,
                0) == 0)
        {
            return;
        }

        Interlocked.Increment(ref _runtimeStatusSubscriptionGeneration);
        _runtimeStatusReader.StatusChanged -= OnRuntimeStatusChanged;
    }

    private void OnRuntimeStatusChanged(
        object? sender,
        PlcTaskRuntimeStatusChangedEventArgs args)
    {
        var subscriptionGeneration =
            Volatile.Read(ref _runtimeStatusSubscriptionGeneration);
        if (Volatile.Read(ref _runtimeStatusSubscriptionActive) == 0)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (Volatile.Read(ref _runtimeStatusSubscriptionActive) == 0
                || subscriptionGeneration
                != Volatile.Read(ref _runtimeStatusSubscriptionGeneration))
            {
                return;
            }

            var device = Devices.SingleOrDefault(candidate => string.Equals(
                candidate.PlcCode,
                args.PlcCode,
                StringComparison.OrdinalIgnoreCase));
            var task = device?.Tasks.SingleOrDefault(candidate => string.Equals(
                candidate.Key,
                args.TaskKey,
                StringComparison.OrdinalIgnoreCase));
            task?.ApplyRuntimeSnapshot(
                _runtimeStatusReader.GetSnapshot(args.PlcCode, args.TaskKey));
        });
    }

    protected virtual void RunOnUiThread(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        OnPropertyChanged(nameof(SelectedDeviceDisplayName));
        OnPropertyChanged(nameof(SelectedDeviceTitle));
    }
}

public sealed class PlcTaskBindingDeviceVm
{
    public PlcTaskBindingDeviceVm(PlcTaskBindingDeviceDto dto)
    {
        NetworkDeviceId = dto.NetworkDeviceId;
        PlcCode = dto.PlcCode;
        DeviceName = dto.DeviceName;
        ModuleId = dto.ModuleId;
        IsDeviceEnabled = dto.IsDeviceEnabled;
        foreach (var task in dto.Tasks)
        {
            Tasks.Add(new PlcTaskBindingTaskVm(task, dto.IsDeviceEnabled));
        }
    }

    public int NetworkDeviceId { get; }

    public string PlcCode { get; }

    public string DeviceName { get; }

    public string ModuleId { get; }

    public bool IsDeviceEnabled { get; }

    public string DeviceStateText => IsDeviceEnabled ? "配置状态：已启用" : "配置状态：未启用";

    public ObservableCollection<PlcTaskBindingTaskVm> Tasks { get; } = [];
}

public sealed class PlcTaskBindingTaskVm : PresentationObservableModelBase
{
    private bool _enabled;
    private readonly bool _isDeviceEnabled;
    private PlcTaskRuntimeState? _runtimeState;
    private DateTimeOffset _displayStateChangedAtUtc;
    private DateTimeOffset? _lastSuccessfulAtUtc;
    private string? _runtimeErrorCode;
    private string? _runtimeExceptionType;

    public PlcTaskBindingTaskVm(PlcTaskBindingItemDto dto, bool isDeviceEnabled)
    {
        Key = dto.Key;
        DisplayName = dto.DisplayName;
        _enabled = dto.Enabled;
        _isDeviceEnabled = isDeviceEnabled;
        _runtimeState = dto.RuntimeState;
        _lastSuccessfulAtUtc = dto.LastSuccessfulAtUtc;
        _runtimeErrorCode = dto.RuntimeErrorCode;
        _runtimeExceptionType = dto.RuntimeExceptionType;
        OriginalEnabled = dto.Enabled;
        HasSavedBinding = dto.HasSavedBinding;
        IsHeartbeatLike = dto.IsHeartbeatLike;
        CanRun = dto.CanRun;
        UnavailableReason = dto.UnavailableReason;
        IsSupportedByCurrentPlc = dto.IsSupportedByCurrentPlc;
        RequiredSignalsText = string.Join(
            "；",
            dto.RequiredSignals.Select(static x => $"{x.SignalKey}/{FormatDirection(x.Direction)}"));
        MissingRequiredSignalsText = dto.MissingRequiredSignals.Count == 0
            ? string.Empty
            : string.Join("；", dto.MissingRequiredSignals.Select(static x => $"{x.SignalKey}/{FormatDirection(x.Direction)}"));
        var initialDisplayState = ResolveDisplayState();
        _displayStateChangedAtUtc = IsRuntimeDisplayState(initialDisplayState)
            ? dto.RuntimeStateChangedAtUtc
              ?? dto.ConfigurationStateChangedAtUtc
              ?? DateTimeOffset.UtcNow
            : dto.ConfigurationStateChangedAtUtc
              ?? DateTimeOffset.UtcNow;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            if (value && !CanRun)
            {
                OnPropertyChanged();
                return;
            }

            _enabled = value;
            OnPropertyChanged();
        }
    }

    public bool OriginalEnabled { get; }

    public bool HasSavedBinding { get; }

    public bool IsHeartbeatLike { get; }

    public bool CanRun { get; }

    public bool IsSupportedByCurrentPlc { get; }

    public string UnavailableReason { get; }

    public string TaskTypeText => IsHeartbeatLike ? "心跳类" : "业务任务";

    public string SourceText => HasSavedBinding ? "已保存" : "绑定缺失（安全关闭）";

    public string RequiredSignalsText { get; }

    public string MissingRequiredSignalsText { get; }

    public string AvailabilityText => CanRun ? "可运行" : UnavailableReason;

    public PlcTaskRuntimeState? RuntimeState => _runtimeState;

    public DateTimeOffset? RuntimeStateChangedAtUtc => _displayStateChangedAtUtc;

    public DateTimeOffset? LastSuccessfulAtUtc => _lastSuccessfulAtUtc;

    public string? RuntimeErrorCode => _runtimeErrorCode;

    public string? RuntimeExceptionType => _runtimeExceptionType;

    public string RuntimeStatusText
    {
        get
        {
            return ResolveDisplayState() switch
            {
                PlcTaskBindingDisplayState.BindingMissing => "绑定缺失",
                PlcTaskBindingDisplayState.Disabled => "已禁用",
                PlcTaskBindingDisplayState.ConfigurationInvalid => "配置无效",
                PlcTaskBindingDisplayState.WaitingForConnection => "等待连接",
                PlcTaskBindingDisplayState.Starting => "启动中",
                PlcTaskBindingDisplayState.Running => "运行中",
                PlcTaskBindingDisplayState.Stopping => "停止中",
                PlcTaskBindingDisplayState.Faulted => "故障",
                _ => "等待 runtime"
            };
        }
    }

    public string NoteText
    {
        get
        {
            var items = new[]
                {
                    !CanRun ? UnavailableReason : null,
                    MissingRequiredSignalsText,
                    _runtimeState == PlcTaskRuntimeState.Faulted
                        ? FormatRuntimeFailure(_runtimeErrorCode, _runtimeExceptionType)
                        : null,
                    $"状态时间={_displayStateChangedAtUtc:yyyy-MM-dd HH:mm:ss} UTC",
                    _lastSuccessfulAtUtc.HasValue
                        ? $"最近成功启动/恢复={_lastSuccessfulAtUtc:yyyy-MM-dd HH:mm:ss} UTC"
                        : "最近成功启动/恢复=尚无"
                }
                .Where(static x => !string.IsNullOrWhiteSpace(x));

            var text = string.Join("；", items);
            return string.IsNullOrWhiteSpace(text) ? "--" : text;
        }
    }

    public void ApplyRuntimeSnapshot(PlcTaskRuntimeSnapshot? snapshot)
    {
        var previousDisplayState = ResolveDisplayState();
        _runtimeState = snapshot?.State;
        _lastSuccessfulAtUtc = snapshot?.LastSuccessfulAtUtc;
        _runtimeErrorCode = snapshot?.ErrorCode;
        _runtimeExceptionType = snapshot?.ExceptionType;
        var nextDisplayState = ResolveDisplayState();
        if (nextDisplayState != previousDisplayState)
        {
            _displayStateChangedAtUtc = snapshot?.StateChangedAtUtc
                ?? DateTimeOffset.UtcNow;
        }
        else if (IsRuntimeDisplayState(nextDisplayState)
                 && snapshot is not null)
        {
            _displayStateChangedAtUtc = snapshot.StateChangedAtUtc;
        }

        OnPropertyChanged(nameof(RuntimeState));
        OnPropertyChanged(nameof(RuntimeStateChangedAtUtc));
        OnPropertyChanged(nameof(LastSuccessfulAtUtc));
        OnPropertyChanged(nameof(RuntimeErrorCode));
        OnPropertyChanged(nameof(RuntimeExceptionType));
        OnPropertyChanged(nameof(RuntimeStatusText));
        OnPropertyChanged(nameof(NoteText));
    }

    private PlcTaskBindingDisplayState ResolveDisplayState()
        => PlcTaskBindingDisplayStateResolver.Resolve(
            HasSavedBinding,
            _isDeviceEnabled,
            OriginalEnabled,
            CanRun,
            _runtimeState);

    private static bool IsRuntimeDisplayState(PlcTaskBindingDisplayState state)
        => state is PlcTaskBindingDisplayState.WaitingForRuntime
            or PlcTaskBindingDisplayState.WaitingForConnection
            or PlcTaskBindingDisplayState.Starting
            or PlcTaskBindingDisplayState.Running
            or PlcTaskBindingDisplayState.Stopping
            or PlcTaskBindingDisplayState.Faulted;

    private static string FormatRuntimeFailure(
        string? errorCode,
        string? exceptionType)
    {
        var safeCode = string.IsNullOrWhiteSpace(errorCode)
            ? PlcTaskRuntimeErrorCodes.TaskFault
            : errorCode;
        return string.IsNullOrWhiteSpace(exceptionType)
            ? $"运行错误码={safeCode}"
            : $"运行错误码={safeCode}，异常类型={exceptionType}";
    }

    private static string FormatDirection(string direction)
        => string.Equals(direction, "Write", StringComparison.OrdinalIgnoreCase)
            ? "写"
            : "读";
}
