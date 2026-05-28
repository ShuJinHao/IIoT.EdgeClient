using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;

public sealed class PlcTaskBindingViewModel : NavigationViewModelBase
{
    private readonly IPlcTaskBindingService _bindingService;
    private readonly IClientPermissionService _permissionService;
    private readonly IPlcTaskBindingConfirmationService _confirmationService;
    private readonly string _moduleId;
    private readonly AsyncCommand _saveCommand;
    private PlcTaskBindingDeviceVm? _selectedDevice;

    public PlcTaskBindingViewModel(
        IPlcTaskBindingService bindingService,
        IClientPermissionService permissionService,
        IPlcTaskBindingConfirmationService confirmationService,
        IAppLanguageService languageService)
        : this(
            bindingService,
            permissionService,
            confirmationService,
            languageService,
            "Hardware.PlcTaskBindingView",
            "Navigation_Title_PlcTaskBinding",
            "任务绑定",
            moduleId: string.Empty)
    {
    }

    public PlcTaskBindingViewModel(
        IPlcTaskBindingService bindingService,
        IClientPermissionService permissionService,
        IPlcTaskBindingConfirmationService confirmationService,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback,
        string moduleId)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _bindingService = bindingService;
        _permissionService = permissionService;
        _confirmationService = confirmationService;
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
            OnPropertyChanged(nameof(SelectedDeviceTasks));
            OnPropertyChanged(nameof(CanSave));
            _saveCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<PlcTaskBindingTaskVm>? SelectedDeviceTasks => SelectedDevice?.Tasks;

    public bool HasDevices => Devices.Count > 0;

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool CanEdit => _permissionService.CanEditHardware;

    public bool CanSave => CanEdit && SelectedDevice is not null;

    public ICommand RefreshCommand { get; }

    public ICommand SaveCommand => _saveCommand;

    public override Task OnActivatedAsync()
        => LoadAsync();

    private async Task LoadAsync()
        => await RunViewTaskAsync(async () =>
        {
            await LoadCoreAsync(SelectedDevice?.NetworkDeviceId).ConfigureAwait(false);
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
            await _bindingService.SaveDeviceBindingsAsync(
                selected.NetworkDeviceId,
                _moduleId,
                states).ConfigureAwait(false);

            await LoadCoreAsync(selected.NetworkDeviceId).ConfigureAwait(false);
            RunOnUiThread(() => SetStatus(GetText("Navigation_PlcTaskBinding_SaveSuccess", "任务绑定已保存。")));
        }, GetText("Navigation_PlcTaskBinding_SaveFailed", "保存 PLC 任务绑定失败。"));

    private async Task LoadCoreAsync(int? selectedDeviceId)
    {
        var devices = await _bindingService.GetModuleDeviceBindingsAsync(_moduleId).ConfigureAwait(false);
        var deviceItems = devices.Select(static x => new PlcTaskBindingDeviceVm(x)).ToArray();

        RunOnUiThread(() =>
        {
            ReplaceItems(Devices, deviceItems);
            OnPropertyChanged(nameof(HasDevices));

            SelectedDevice = selectedDeviceId is null
                ? Devices.FirstOrDefault()
                : Devices.FirstOrDefault(x => x.NetworkDeviceId == selectedDeviceId.Value) ?? Devices.FirstOrDefault();

            SetStatus(Devices.Count == 0
                ? GetText("Navigation_PlcTaskBinding_NoDevices", "当前模块暂无 PLC 设备。")
                : FormatText("Navigation_PlcTaskBinding_DeviceCountFormat", "已加载 {0} 台 PLC 的任务绑定。", Devices.Count));
        });
    }

    private static void RunOnUiThread(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }
}

public sealed class PlcTaskBindingDeviceVm
{
    public PlcTaskBindingDeviceVm(PlcTaskBindingDeviceDto dto)
    {
        NetworkDeviceId = dto.NetworkDeviceId;
        DeviceName = dto.DeviceName;
        ModuleId = dto.ModuleId;
        IsDeviceEnabled = dto.IsDeviceEnabled;
        foreach (var task in dto.Tasks)
        {
            Tasks.Add(new PlcTaskBindingTaskVm(task));
        }
    }

    public int NetworkDeviceId { get; }

    public string DeviceName { get; }

    public string ModuleId { get; }

    public bool IsDeviceEnabled { get; }

    public string DeviceStateText => IsDeviceEnabled ? "启用" : "停用";

    public ObservableCollection<PlcTaskBindingTaskVm> Tasks { get; } = [];
}

public sealed class PlcTaskBindingTaskVm : ObservableModelBase
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

    public string SourceText => HasSavedBinding ? "已保存" : "默认值";

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
