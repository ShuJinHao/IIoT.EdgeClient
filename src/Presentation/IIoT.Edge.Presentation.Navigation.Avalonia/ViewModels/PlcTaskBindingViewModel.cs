using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class PlcTaskBindingViewModel : NavigationPageViewModelBase
{
    private const string ViewIdSuffix = ".PlcTaskBindingView";

    private readonly IPlcTaskBindingService _bindingService;
    private readonly IClientPermissionService _permissionService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaDialogService _dialogService;
    private readonly IAvaloniaDispatcherService _dispatcherService;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _saveCommand;
    private readonly string _moduleId;
    private bool _isSubscribed;

    public PlcTaskBindingViewModel(
        IPlcTaskBindingService bindingService,
        IClientPermissionService permissionService,
        IAvaloniaLanguageService languageService,
        IAvaloniaDialogService dialogService,
        IAvaloniaDispatcherService dispatcherService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _bindingService = bindingService;
        _permissionService = permissionService;
        _languageService = languageService;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _moduleId = ResolveModuleId(viewId);
        _refreshCommand = new AsyncRelayCommand(LoadAsync);
        _saveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
    }

    public ObservableCollection<PlcTaskBindingDeviceRow> Devices { get; } = [];

    [ObservableProperty]
    private PlcTaskBindingDeviceRow? selectedDevice;

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    public ObservableCollection<PlcTaskBindingTaskRow>? SelectedDeviceTasks => SelectedDevice?.Tasks;

    public bool HasDevices => Devices.Count > 0;

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool CanEdit => _permissionService.CanEditHardware;

    public bool IsReadOnly => !CanEdit;

    public bool CanSave => CanEdit && SelectedDevice is not null;

    public IAsyncRelayCommand RefreshCommand => _refreshCommand;

    public IAsyncRelayCommand SaveCommand => _saveCommand;

    public override async Task OnActivatedAsync()
    {
        if (!_isSubscribed)
        {
            _permissionService.PermissionStateChanged += HandlePermissionStateChanged;
            _isSubscribed = true;
        }

        await LoadAsync();
        RefreshPermissionState();
    }

    public override Task OnDeactivatedAsync()
    {
        if (_isSubscribed)
        {
            _permissionService.PermissionStateChanged -= HandlePermissionStateChanged;
            _isSubscribed = false;
        }

        return Task.CompletedTask;
    }

    partial void OnSelectedDeviceChanged(PlcTaskBindingDeviceRow? value)
    {
        OnPropertyChanged(nameof(SelectedDeviceTasks));
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(CanSave));
        _saveCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAsync()
    {
        try
        {
            await LoadCoreAsync(SelectedDevice?.NetworkDeviceId);
        }
        catch (Exception ex)
        {
            FeedbackMessage = Format("Navigation_PlcTaskBinding_LoadFailed", "加载 PLC 任务绑定失败：{0}", ex.Message);
        }
    }

    private async Task SaveAsync()
    {
        if (!CanEdit)
        {
            var message = Text("Navigation_PlcTaskBinding_NoPermission", "当前用户没有硬件配置权限。");
            FeedbackMessage = message;
            await _dialogService.ShowInfoAsync(ViewTitle, message);
            return;
        }

        var selected = SelectedDevice;
        if (selected is null)
        {
            var message = Text("Navigation_PlcTaskBinding_SelectDeviceFirst", "请先选择 PLC 设备。");
            FeedbackMessage = message;
            await _dialogService.ShowInfoAsync(ViewTitle, message);
            return;
        }

        var disabledHeartbeatTasks = selected.Tasks
            .Where(static task => task.IsHeartbeatLike && task.OriginalEnabled && !task.Enabled)
            .Select(static task => task.DisplayName)
            .ToArray();

        if (disabledHeartbeatTasks.Length > 0)
        {
            var confirmed = await _dialogService.ConfirmAsync(
                Text("Navigation_PlcTaskBinding_DisableHeartbeatTitle", "确认禁用心跳任务"),
                Format(
                    "Navigation_PlcTaskBinding_DisableHeartbeatMessageFormat",
                    "即将禁用 PLC“{0}”的心跳类任务：{1}。禁用后对应外部系统可用性判断可能失效，是否继续保存？",
                    selected.DeviceName,
                    string.Join("、", disabledHeartbeatTasks)));

            if (!confirmed)
            {
                FeedbackMessage = Text("Navigation_PlcTaskBinding_SaveCanceled", "已取消保存。");
                return;
            }
        }

        try
        {
            var taskStates = selected.Tasks.ToDictionary(
                static task => task.Key,
                static task => task.Enabled,
                StringComparer.OrdinalIgnoreCase);

            await _bindingService.SaveDeviceBindingsAsync(
                selected.NetworkDeviceId,
                _moduleId,
                taskStates);

            await LoadCoreAsync(selected.NetworkDeviceId);
            FeedbackMessage = Text("Navigation_PlcTaskBinding_SaveSuccess", "任务绑定已保存。");
        }
        catch (Exception ex)
        {
            FeedbackMessage = Format("Navigation_PlcTaskBinding_SaveFailed", "保存 PLC 任务绑定失败：{0}", ex.Message);
        }
    }

    private async Task LoadCoreAsync(int? selectedDeviceId)
    {
        var devices = await _bindingService.GetModuleDeviceBindingsAsync(_moduleId);
        var rows = devices.Select(static device => new PlcTaskBindingDeviceRow(device)).ToArray();

        Devices.Clear();
        foreach (var row in rows)
        {
            Devices.Add(row);
        }

        SelectedDevice = selectedDeviceId is null
            ? Devices.FirstOrDefault()
            : Devices.FirstOrDefault(device => device.NetworkDeviceId == selectedDeviceId.Value) ?? Devices.FirstOrDefault();

        OnPropertyChanged(nameof(HasDevices));
        FeedbackMessage = Devices.Count == 0
            ? Text("Navigation_PlcTaskBinding_NoDevices", "当前模块暂无 PLC 设备。")
            : Format("Navigation_PlcTaskBinding_DeviceCountFormat", "已加载 {0} 台 PLC 的任务绑定。", Devices.Count);
    }

    private void HandlePermissionStateChanged()
        => _dispatcherService.Post(RefreshPermissionState);

    private void RefreshPermissionState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(CanSave));
        _saveCommand.NotifyCanExecuteChanged();
    }

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private string Format(string key, string fallback, params object[] args)
        => string.Format(Text(key, fallback), args);

    private static string ResolveModuleId(string viewId)
    {
        if (viewId.EndsWith(ViewIdSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return viewId[..^ViewIdSuffix.Length];
        }

        var separator = viewId.IndexOf('.');
        return separator > 0 ? viewId[..separator] : string.Empty;
    }
}

public sealed class PlcTaskBindingDeviceRow
{
    public PlcTaskBindingDeviceRow(PlcTaskBindingDeviceDto dto)
    {
        NetworkDeviceId = dto.NetworkDeviceId;
        DeviceName = dto.DeviceName;
        ModuleId = dto.ModuleId;
        IsDeviceEnabled = dto.IsDeviceEnabled;

        foreach (var task in dto.Tasks)
        {
            Tasks.Add(new PlcTaskBindingTaskRow(task));
        }
    }

    public int NetworkDeviceId { get; }

    public string DeviceName { get; }

    public string ModuleId { get; }

    public bool IsDeviceEnabled { get; }

    public string DeviceStateText => IsDeviceEnabled ? "启用" : "停用";

    public string DisplayText => $"{DeviceName}（{DeviceStateText}）";

    public ObservableCollection<PlcTaskBindingTaskRow> Tasks { get; } = [];

    public override string ToString() => DisplayText;
}

public sealed class PlcTaskBindingTaskRow : ObservableObject
{
    private bool _enabled;

    public PlcTaskBindingTaskRow(PlcTaskBindingItemDto dto)
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
        RequiredSignalsText = FormatSignals(dto.RequiredSignals);
        MissingRequiredSignalsText = dto.MissingRequiredSignals.Count == 0
            ? string.Empty
            : FormatSignals(dto.MissingRequiredSignals);
    }

    public string Key { get; }

    public string DisplayName { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value && !CanRun)
            {
                OnPropertyChanged();
                return;
            }

            SetProperty(ref _enabled, value);
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

    public string AvailabilityText => CanRun
        ? "可运行"
        : string.IsNullOrWhiteSpace(UnavailableReason) ? "不可运行" : UnavailableReason;

    private static string FormatSignals(IEnumerable<TaskRequiredSignal> signals)
        => string.Join("；", signals.Select(static signal => $"{signal.SignalKey}/{FormatDirection(signal.Direction)}"));

    private static string FormatDirection(string direction)
        => string.Equals(direction, "Write", StringComparison.OrdinalIgnoreCase) ? "写" : "读";
}
