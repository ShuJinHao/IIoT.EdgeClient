using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.HardwareConfig.ViewModels;

public sealed partial class HardwareConfigViewModel : NavigationPageViewModelBase
{
    private readonly IAvaloniaLanguageService _languageService;
    private int _nextNetworkDeviceId = 3;
    private int _nextSerialDeviceId = 3;
    private int _nextIoMappingId = 4;

    public HardwareConfigViewModel(
        IAvaloniaLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _languageService = languageService;
        NetworkDevices =
        [
            new NetworkDeviceRow(1, "PLC-01", "PLC", "S7-1200", "Homogenization", "192.168.1.10", 102, 0, "Read", "Write", 3000, true, "主线 PLC"),
            new NetworkDeviceRow(2, "Scanner-01", "Scanner", "TCP", "Homogenization", "192.168.1.20", 9000, 0, "TRIGGER", string.Empty, 3000, true, "工位扫码器")
        ];
        SerialDevices =
        [
            new SerialDeviceRow(1, "Scale-01", "Scale", "COM1", 9600, 8, "One", "None", "READ", string.Empty, true, "前段称重"),
            new SerialDeviceRow(2, "Printer-01", "Printer", "COM2", 115200, 8, "One", "None", "PRINT", string.Empty, false, "标签打印")
        ];
        IoMappings =
        [
            new IoMappingRow(1, 1, "DB1.DBX0.0", 1, "交互", "启动请求", "Bool", "Read", true),
            new IoMappingRow(2, 1, "DB1.DBX0.1", 1, "交互", "完成应答", "Bool", "Write", true),
            new IoMappingRow(3, 1, "DB1.DBD10", 2, "数据点", "当前重量", "Real", "Read", false)
        ];

        SelectedNetworkDevice = NetworkDevices.FirstOrDefault();
        RefreshFilteredIoMappings();
    }

    public ObservableCollection<NetworkDeviceRow> NetworkDevices { get; }

    public ObservableCollection<SerialDeviceRow> SerialDevices { get; }

    public ObservableCollection<IoMappingRow> IoMappings { get; }

    public ObservableCollection<IoMappingRow> FilteredIoMappings { get; } = [];

    public IReadOnlyList<string> NetworkDeviceTypes { get; } = ["PLC", "Scanner", "Camera"];

    public IReadOnlyList<string> NetworkDeviceModels { get; } = ["S7-1200", "S7-1500", "ModbusTcp", "TCP"];

    public IReadOnlyList<string> SerialStopBits { get; } = ["One", "OnePointFive", "Two"];

    public IReadOnlyList<string> SerialParities { get; } = ["None", "Odd", "Even"];

    public IReadOnlyList<string> IoDataTypes { get; } = ["Bool", "Int16", "Int32", "Real", "String"];

    public IReadOnlyList<string> IoDirections { get; } = ["Read", "Write"];

    public IReadOnlyList<string> IoBusinessGroups { get; } = ["Interaction", "DataPoint"];

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
    private bool isDialogOpen;

    [ObservableProperty]
    private string dialogTitleResourceKey = "Navigation_Dialog_Title_Pending";

    [ObservableProperty]
    private string dialogMessageResourceKey = "Navigation_Dialog_Message_Pending";

    [ObservableProperty]
    private string pendingOperationResourceKey = "Navigation_Status_ReadOnlyValidation";

    public bool CanEdit => true;

    public string DialogTitle => Text(DialogTitleResourceKey, DialogTitleResourceKey);

    public string DialogMessage => Text(DialogMessageResourceKey, DialogMessageResourceKey);

    public string PendingOperationText => Text(PendingOperationResourceKey, PendingOperationResourceKey);

    partial void OnDialogTitleResourceKeyChanged(string value) => OnPropertyChanged(nameof(DialogTitle));

    partial void OnDialogMessageResourceKeyChanged(string value) => OnPropertyChanged(nameof(DialogMessage));

    partial void OnPendingOperationResourceKeyChanged(string value) => OnPropertyChanged(nameof(PendingOperationText));

    partial void OnSelectedNetworkDeviceChanged(NetworkDeviceRow? value)
    {
        SelectedIoMapping = null;
        RefreshFilteredIoMappings();
        DeleteNetworkDeviceCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddNetworkDevice()
    {
        var row = new NetworkDeviceRow(
            _nextNetworkDeviceId++,
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
        foreach (var mapping in IoMappings.Where(item => item.NetworkDeviceId == selected.Id).ToArray())
        {
            IoMappings.Remove(mapping);
        }

        SelectedNetworkDevice = NetworkDevices.FirstOrDefault();
        RefreshFilteredIoMappings();
    }

    private bool CanDeleteNetworkDevice() => SelectedNetworkDevice is not null;

    [RelayCommand]
    private void AddSerialDevice()
    {
        var row = new SerialDeviceRow(
            _nextSerialDeviceId++,
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
        if (PendingOperationResourceKey == "Navigation_Status_SavePending")
        {
            IsDialogOpen = false;
            return;
        }

        if (SelectedNetworkDevice is null)
        {
            return;
        }

        var group = PendingOperationResourceKey == "Navigation_Status_AddInteractionPending" ? "交互" : "数据点";
        var row = new IoMappingRow(
            _nextIoMappingId++,
            SelectedNetworkDevice.Id,
            string.Empty,
            1,
            group,
            "新信号",
            "Bool",
            "Read",
            group == "交互");
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
    private void Save()
    {
        DialogTitleResourceKey = "Navigation_Dialog_Title_SaveHardwareConfig";
        DialogMessageResourceKey = "Navigation_Dialog_Message_SaveHardwareConfigReadonly";
        PendingOperationResourceKey = "Navigation_Status_SavePending";
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDialog()
    {
        IsDialogOpen = false;
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
            .OrderBy(item => item.BusinessGroup, StringComparer.Ordinal)
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
}
