using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.HardwareConfig.ViewModels;

public sealed partial class HardwareConfigViewModel : NavigationPageViewModelBase
{
    private readonly IHardwareConfigCrudService _hardwareConfigService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaDialogService _dialogService;
    private readonly AsyncRelayCommand _loadCommand;
    private readonly AsyncRelayCommand _saveCommand;
    private int _nextNetworkDeviceId;
    private int _nextSerialDeviceId;
    private int _nextIoMappingId;

    public HardwareConfigViewModel(
        IHardwareConfigCrudService hardwareConfigService,
        IAvaloniaLanguageService languageService,
        IAvaloniaDialogService dialogService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _hardwareConfigService = hardwareConfigService;
        _languageService = languageService;
        _dialogService = dialogService;
        _loadCommand = new AsyncRelayCommand(LoadAsync);
        _saveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public ObservableCollection<NetworkDeviceRow> NetworkDevices { get; } = [];

    public ObservableCollection<SerialDeviceRow> SerialDevices { get; } = [];

    public ObservableCollection<IoMappingRow> IoMappings { get; } = [];

    public ObservableCollection<IoMappingRow> FilteredIoMappings { get; } = [];

    public ObservableCollection<IoMappingCandidateRow> CandidateIoSignals { get; } = [];

    public IReadOnlyList<string> NetworkDeviceTypes { get; } = ["PLC", "Scanner", "Camera", "Tester"];

    public IReadOnlyList<string> NetworkDeviceModels { get; } = ["S7-1200", "S7-1500", "ModbusTcp", "TCP"];

    public IReadOnlyList<string> SerialStopBits { get; } = ["One", "OnePointFive", "Two"];

    public IReadOnlyList<string> SerialParities { get; } = ["None", "Odd", "Even"];

    public IReadOnlyList<string> IoDataTypes { get; } = ["Bool", "Int16", "Int32", "Real", "String"];

    public IReadOnlyList<string> IoDirections { get; } = ["Read", "Write"];

    public IReadOnlyList<string> IoBusinessGroups { get; } = ["Interaction", "DataPoint"];

    public bool CanEdit => true;

    public string DialogTitle => Text(DialogTitleResourceKey, DialogTitleResourceKey);

    public string DialogMessage => Text(DialogMessageResourceKey, DialogMessageResourceKey);

    public string PendingOperationText => Text(PendingOperationResourceKey, PendingOperationResourceKey);

    public IAsyncRelayCommand LoadCommand => _loadCommand;

    public IAsyncRelayCommand SaveCommand => _saveCommand;

    [ObservableProperty]
    private int selectedTabIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteNetworkDeviceCommand))]
    private NetworkDeviceRow? selectedNetworkDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSerialDeviceCommand))]
    private SerialDeviceRow? selectedSerialDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteIoMappingCommand))]
    private IoMappingRow? selectedIoMapping;

    [ObservableProperty]
    private IoMappingCandidateRow? selectedCandidateIoSignal;

    [ObservableProperty]
    private bool isDialogOpen;

    [ObservableProperty]
    private string dialogTitleResourceKey = "Navigation_Dialog_Title_Pending";

    [ObservableProperty]
    private string dialogMessageResourceKey = "Navigation_Dialog_Message_Pending";

    [ObservableProperty]
    private string pendingOperationResourceKey = "Navigation_Status_ReadOnlyValidation";

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    public override async Task OnActivatedAsync()
    {
        await LoadAsync();
    }

    partial void OnDialogTitleResourceKeyChanged(string value) => OnPropertyChanged(nameof(DialogTitle));

    partial void OnDialogMessageResourceKeyChanged(string value) => OnPropertyChanged(nameof(DialogMessage));

    partial void OnPendingOperationResourceKeyChanged(string value) => OnPropertyChanged(nameof(PendingOperationText));

    partial void OnSelectedNetworkDeviceChanged(NetworkDeviceRow? value)
    {
        SelectedIoMapping = null;
        _ = LoadSelectedDeviceMappingsAsync();
    }

    [RelayCommand]
    private void AddNetworkDevice()
    {
        var row = new NetworkDeviceRow(
            --_nextNetworkDeviceId,
            "网络设备",
            "PLC",
            "S7-1200",
            "Homogenization",
            "192.168.1.100",
            102,
            0,
            string.Empty,
            string.Empty,
            3000,
            false,
            string.Empty);
        NetworkDevices.Add(row);
        SelectedNetworkDevice = row;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteNetworkDevice))]
    private void DeleteNetworkDevice()
    {
        if (SelectedNetworkDevice is not { } selected)
        {
            return;
        }

        NetworkDevices.Remove(selected);
        if (SelectedNetworkDevice?.Id == selected.Id)
        {
            IoMappings.Clear();
            FilteredIoMappings.Clear();
        }

        SelectedNetworkDevice = NetworkDevices.FirstOrDefault();
    }

    private bool CanDeleteNetworkDevice() => SelectedNetworkDevice is not null;

    [RelayCommand]
    private void AddSerialDevice()
    {
        var row = new SerialDeviceRow(
            --_nextSerialDeviceId,
            "串口设备",
            "称重设备",
            "COM1",
            9600,
            8,
            "One",
            "None",
            string.Empty,
            string.Empty,
            false,
            string.Empty);
        SerialDevices.Add(row);
        SelectedSerialDevice = row;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSerialDevice))]
    private void DeleteSerialDevice()
    {
        if (SelectedSerialDevice is not { } selected)
        {
            return;
        }

        SerialDevices.Remove(selected);
        SelectedSerialDevice = SerialDevices.FirstOrDefault();
    }

    private bool CanDeleteSerialDevice() => SelectedSerialDevice is not null;

    [RelayCommand]
    private void OpenAddInteractionMappingDialog()
    {
        DialogTitleResourceKey = "Navigation_Dialog_AddIoPoint";
        DialogMessageResourceKey = "Navigation_Dialog_AddInteraction";
        PendingOperationResourceKey = "Navigation_Status_AddInteractionPending";
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void OpenAddDataPointMappingDialog()
    {
        DialogTitleResourceKey = "Navigation_Dialog_AddIoPoint";
        DialogMessageResourceKey = "Navigation_Dialog_AddDataPoint";
        PendingOperationResourceKey = "Navigation_Status_AddDataPointPending";
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void ConfirmDialog()
    {
        if (SelectedNetworkDevice is null)
        {
            IsDialogOpen = false;
            return;
        }

        var selectedCandidate = SelectCandidateForPendingOperation();
        var row = selectedCandidate is null
            ? CreateBlankMappingForPendingOperation()
            : new IoMappingRow(--_nextIoMappingId, SelectedNetworkDevice.Id, selectedCandidate.Source);

        IoMappings.Add(row);
        SelectedIoMapping = row;
        RefreshFilteredIoMappings();
        IsDialogOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteIoMapping))]
    private void DeleteIoMapping()
    {
        if (SelectedIoMapping is not { } selected)
        {
            return;
        }

        IoMappings.Remove(selected);
        SelectedIoMapping = null;
        RefreshFilteredIoMappings();
    }

    private bool CanDeleteIoMapping() => SelectedIoMapping is not null;

    [RelayCommand]
    private void CloseDialog()
    {
        IsDialogOpen = false;
    }

    private async Task LoadAsync()
    {
        var selectedId = SelectedNetworkDevice?.Id;
        try
        {
            var result = await _hardwareConfigService.LoadAsync();
            NetworkDevices.Clear();
            foreach (var device in result.NetworkDevices.Select(static device => new NetworkDeviceRow(device)))
            {
                NetworkDevices.Add(device);
            }

            SerialDevices.Clear();
            foreach (var device in result.SerialDevices.Select(static device => new SerialDeviceRow(device)))
            {
                SerialDevices.Add(device);
            }

            _nextNetworkDeviceId = Math.Min(0, NetworkDevices.Select(static x => x.Id).DefaultIfEmpty(0).Min());
            _nextSerialDeviceId = Math.Min(0, SerialDevices.Select(static x => x.Id).DefaultIfEmpty(0).Min());
            SelectedSerialDevice = SerialDevices.FirstOrDefault();
            SelectedNetworkDevice = selectedId is null
                ? NetworkDevices.FirstOrDefault()
                : NetworkDevices.FirstOrDefault(device => device.Id == selectedId.Value) ?? NetworkDevices.FirstOrDefault();

            if (SelectedNetworkDevice is null)
            {
                IoMappings.Clear();
                FilteredIoMappings.Clear();
                CandidateIoSignals.Clear();
            }
            else
            {
                await LoadSelectedDeviceMappingsAsync();
            }

            FeedbackMessage = Format("Navigation_Hardware_Loaded", "已加载 {0} 个网络设备、{1} 个串口设备。", NetworkDevices.Count, SerialDevices.Count);
        }
        catch (Exception ex)
        {
            FeedbackMessage = Format("Navigation_Hardware_LoadFailed", "硬件配置加载失败：{0}", ex.Message);
        }
    }

    private async Task LoadSelectedDeviceMappingsAsync()
    {
        IoMappings.Clear();
        FilteredIoMappings.Clear();
        CandidateIoSignals.Clear();
        SelectedCandidateIoSignal = null;

        var selected = SelectedNetworkDevice;
        if (selected is null || selected.Id <= 0)
        {
            return;
        }

        try
        {
            var mappingResult = await _hardwareConfigService.LoadIoMappingsAsync(selected.Id);
            foreach (var mapping in mappingResult.Items.Select(static mapping => new IoMappingRow(mapping)))
            {
                IoMappings.Add(mapping);
            }

            _nextIoMappingId = Math.Min(0, IoMappings.Select(static x => x.Id).DefaultIfEmpty(0).Min());

            var templateInfo = await _hardwareConfigService.GetModuleTemplateInfoAsync(selected.ToVm());
            foreach (var signal in templateInfo.CandidateSignals.Select(static signal => new IoMappingCandidateRow(signal)))
            {
                CandidateIoSignals.Add(signal);
            }

            SelectedCandidateIoSignal = CandidateIoSignals.FirstOrDefault();
            RefreshFilteredIoMappings();
        }
        catch (Exception ex)
        {
            FeedbackMessage = Format("Navigation_Hardware_LoadMappingsFailed", "I/O 映射加载失败：{0}", ex.Message);
        }
    }

    private async Task SaveAsync()
    {
        var confirmed = await _dialogService.ConfirmAsync(
            Text("Navigation_Dialog_Title_SaveHardwareConfig", "保存硬件配置"),
            Text("Navigation_Dialog_Message_SaveHardwareConfigConfirm", "即将保存网络设备、串口设备和当前网络设备的 I/O 映射，是否继续？"));

        if (!confirmed)
        {
            FeedbackMessage = Text("Navigation_Hardware_SaveCanceled", "已取消保存。");
            return;
        }

        var selectedId = SelectedNetworkDevice?.Id ?? 0;
        CrudOperationResult result;
        try
        {
            result = await _hardwareConfigService.SaveAsync(
                NetworkDevices.Select(static device => device.ToVm()).ToArray(),
                SerialDevices.Select(static device => device.ToVm()).ToArray(),
                selectedId,
                FilteredIoMappings.Select(static mapping => mapping.ToVm()).ToArray());
        }
        catch (Exception ex)
        {
            FeedbackMessage = Format("Navigation_Hardware_SaveFailed", "硬件配置保存失败：{0}", ex.Message);
            return;
        }

        FeedbackMessage = string.IsNullOrWhiteSpace(result.Message)
            ? result.IsSuccess ? Text("Navigation_Hardware_SaveSuccess", "硬件配置已保存。") : Text("Navigation_Hardware_SaveFailedShort", "硬件配置保存失败。")
            : result.Message;

        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private IoMappingCandidateRow? SelectCandidateForPendingOperation()
    {
        if (SelectedCandidateIoSignal is { } selected
            && MatchesPendingOperation(selected.Source))
        {
            return selected;
        }

        return CandidateIoSignals.FirstOrDefault(candidate => MatchesPendingOperation(candidate.Source));
    }

    private bool MatchesPendingOperation(ModuleIoTemplateEntry candidate)
    {
        var pendingInteraction = PendingOperationResourceKey == "Navigation_Status_AddInteractionPending";
        var isInteraction = Contains(candidate.Category, "Interaction")
            || Contains(candidate.Category, "交互")
            || Contains(candidate.BusinessGroup, "Interaction")
            || Contains(candidate.BusinessGroup, "交互");
        return pendingInteraction == isInteraction;
    }

    private IoMappingRow CreateBlankMappingForPendingOperation()
    {
        var isInteraction = PendingOperationResourceKey == "Navigation_Status_AddInteractionPending";
        return new IoMappingRow(
            --_nextIoMappingId,
            SelectedNetworkDevice!.Id,
            string.Empty,
            string.Empty,
            1,
            isInteraction ? "Interaction" : "SingleRead",
            isInteraction ? "交互" : "数据点",
            "新信号",
            "Bool",
            "Read",
            IoMappings.Count + 1,
            null,
            isInteraction);
    }

    private void RefreshFilteredIoMappings()
    {
        FilteredIoMappings.Clear();
        if (SelectedNetworkDevice is null)
        {
            return;
        }

        foreach (var item in IoMappings
            .Where(item => item.NetworkDeviceId == SelectedNetworkDevice.Id)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.BusinessGroup, StringComparer.Ordinal)
            .ThenBy(item => item.SignalName, StringComparer.Ordinal))
        {
            FilteredIoMappings.Add(item);
        }
    }

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private string Format(string key, string fallback, params object[] args)
        => string.Format(Text(key, fallback), args);

    private static bool Contains(string? value, string token)
        => value?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
}
