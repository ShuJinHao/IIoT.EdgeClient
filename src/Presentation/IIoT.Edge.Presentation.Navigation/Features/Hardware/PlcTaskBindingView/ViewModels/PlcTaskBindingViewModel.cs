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
    private readonly string _moduleId;
    private readonly AsyncCommand _saveCommand;
    private PlcTaskBindingDeviceVm? _selectedDevice;
    private bool _isDeviceSelectionSubscribed;

    public PlcTaskBindingViewModel(
        IPlcTaskBindingService bindingService,
        IPlcTaskBindingTransactionService bindingTransactionService,
        IClientPermissionService permissionService,
        IPlcTaskBindingConfirmationService confirmationService,
        IAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService)
        : this(
            bindingService,
            bindingTransactionService,
            permissionService,
            confirmationService,
            languageService,
            deviceSelectionService,
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
        return LoadAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        UnsubscribeDeviceSelection();
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
            Tasks.Add(new PlcTaskBindingTaskVm(task));
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

    public PlcTaskBindingTaskVm(PlcTaskBindingItemDto dto)
    {
        Key = dto.Key;
        DisplayName = dto.DisplayName;
        _enabled = dto.Enabled;
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

    public string NoteText
    {
        get
        {
            var items = new[]
                {
                    !CanRun ? UnavailableReason : null,
                    MissingRequiredSignalsText
                }
                .Where(static x => !string.IsNullOrWhiteSpace(x));

            var text = string.Join("；", items);
            return string.IsNullOrWhiteSpace(text) ? "--" : text;
        }
    }

    private static string FormatDirection(string direction)
        => string.Equals(direction, "Write", StringComparison.OrdinalIgnoreCase)
            ? "写"
            : "读";
}
