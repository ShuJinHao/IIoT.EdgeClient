using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

public sealed class IoViewViewModel : NavigationPageViewModelBase
{
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IIoViewSafeInteractionPort _interactionPort;
    private readonly AsyncRelayCommand _manualReadCommand;
    private IoNetworkDeviceModel? _selectedDevice;
    private bool _isConnected;
    private string _feedbackMessage = string.Empty;
    private int _readVersion;

    public IoViewViewModel(
        IAvaloniaLanguageService languageService,
        IIoViewSafeInteractionPort? interactionPort = null,
        string viewId = "Hardware.IOView",
        string titleResourceKey = "Navigation_Title_IoInteract",
        string titleFallback = "IO 交互")
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _languageService = languageService;
        _interactionPort = interactionPort ?? new NoopIoViewSafeInteractionPort();
        RefreshDevicesCommand = new RelayCommand(LoadDevices);
        _manualReadCommand = new AsyncRelayCommand(ManualReadSelectedDataAsync, () => SelectedDevice is not null);
        LoadDevices();
    }

    public ObservableCollection<IoNetworkDeviceModel> Devices { get; } = [];

    public ObservableCollection<IoInteractionRowModel> InteractionRows { get; } = [];

    public ObservableCollection<IoDataSectionModel> DataSections { get; } = [];

    public ObservableCollection<IoContinuousReadMatrixSectionModel> ArraySections { get; } = [];

    public bool HasInteractionRows => InteractionRows.Count > 0;

    public bool HasDataSections => DataSections.Count > 0;

    public bool HasArraySections => ArraySections.Count > 0;

    public bool HasNoSignals => !HasInteractionRows && !HasDataSections && !HasArraySections && SelectedDevice is not null;

    public IoNetworkDeviceModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                IsConnected = false;
                LoadMappings();
                _manualReadCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasSelectedDevice));
            }
        }
    }

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public string FeedbackMessage
    {
        get => _feedbackMessage;
        private set => SetProperty(ref _feedbackMessage, value);
    }

    public RelayCommand RefreshDevicesCommand { get; }

    public IAsyncRelayCommand ManualReadCommand => _manualReadCommand;

    public override string ViewTitle
        => Text("Navigation_Title_IoInteract", "IO 交互");

    public override Task OnActivatedAsync()
    {
        LoadDevices();
        return Task.CompletedTask;
    }

    private void LoadDevices()
    {
        var selectedId = SelectedDevice?.Id;
        Devices.Clear();
        Devices.Add(new IoNetworkDeviceModel { Id = 1, DeviceName = Text("Navigation_Io_DemoDeviceA", "PLC 交互站 A"), DeviceCode = "PLC-A" });
        Devices.Add(new IoNetworkDeviceModel { Id = 2, DeviceName = Text("Navigation_Io_DemoDeviceB", "PLC 数据站 B"), DeviceCode = "PLC-B" });

        SelectedDevice = Devices.FirstOrDefault(device => device.Id == selectedId) ?? Devices.FirstOrDefault();
        LoadMappings();
    }

    private void LoadMappings()
    {
        InteractionRows.Clear();
        DataSections.Clear();
        ArraySections.Clear();

        if (SelectedDevice is null)
        {
            NotifySignalCollectionsChanged();
            return;
        }

        BuildInteractionRows();
        BuildDataSections();
        BuildContinuousReadMatrix();
        RefreshPreviewValues();
        NotifySignalCollectionsChanged();
        FeedbackMessage = Text("Navigation_Io_SafeSkeletonMessage", "当前为 Avalonia I/O 骨架，手动读取和写入不会连接真实 PLC。");
    }

    private void BuildInteractionRows()
    {
        var start = CreateInteractionRow("启动", "D100", "D101", 1);
        var stop = CreateInteractionRow("停止", "D102", "D103", 0);
        InteractionRows.Add(start);
        InteractionRows.Add(stop);
    }

    private IoInteractionRowModel CreateInteractionRow(string businessGroup, string plcAddress, string hostAddress, int value)
    {
        var row = new IoInteractionRowModel
        {
            BusinessGroup = businessGroup,
            ListSeparator = Text("Navigation_ListSeparator", "、")
        };
        row.AddPlcSignal(new IoSignalModel
        {
            SignalKey = $"{businessGroup}.Request",
            SignalName = $"{businessGroup}请求",
            PlcAddress = plcAddress,
            Direction = "Read",
            DirectionText = Text("Navigation_Io_Direction_PlcToHost", "PLC 到上位机")
        });
        row.AddHostSignal(new IoSignalModel
        {
            SignalKey = $"{businessGroup}.Reply",
            SignalName = $"{businessGroup}应答",
            PlcAddress = hostAddress,
            Direction = "Write",
            DirectionText = Text("Navigation_Io_Direction_HostToPlc", "上位机到 PLC")
        });
        row.WriteValue = value;
        row.WriteCommand = new AsyncRelayCommand(() => WriteInteractionRowAsync(row), () => row.CanWrite && SelectedDevice is not null);
        return row;
    }

    private void BuildDataSections()
    {
        var status = new IoDataSectionModel
        {
            Category = "SingleRead",
            BusinessGroup = "设备状态",
            Title = "单点读取 / 设备状态",
            CanManualRead = true
        };
        status.Signals.Add(new IoSignalModel { SignalKey = "Status.Run", SignalName = "运行状态", PlcAddress = "D200" });
        status.Signals.Add(new IoSignalModel { SignalKey = "Status.Alarm", SignalName = "报警码", PlcAddress = "D201" });
        DataSections.Add(status);

        var product = new IoDataSectionModel
        {
            Category = "SingleRead",
            BusinessGroup = "生产数据",
            Title = "单点读取 / 生产数据",
            CanManualRead = true
        };
        product.Signals.Add(new IoSignalModel { SignalKey = "Product.OkCount", SignalName = "良品数", PlcAddress = "D210" });
        product.Signals.Add(new IoSignalModel { SignalKey = "Product.NgCount", SignalName = "不良数", PlcAddress = "D211" });
        DataSections.Add(product);
    }

    private void BuildContinuousReadMatrix()
    {
        var section = new IoContinuousReadMatrixSectionModel(
            Text("Navigation_Io_CollapseDetails", "收起明细"),
            Text("Navigation_Io_ViewDetails", "查看明细"))
        {
            Category = "ContinuousRead",
            BusinessGroup = "连续读取",
            Title = "连续读取 / 工位矩阵",
            EmptySummary = Text("Navigation_Io_NoContinuousValues", "暂无连续值"),
            SummaryFormat = Text("Navigation_Io_ArraySummaryFormat", "{0} 行 x {1} 项")
        };

        section.Columns.Add(CreateMatrixColumn("Station.Index", "工位", "D300", ["1", "2", "3", "4"]));
        section.Columns.Add(CreateMatrixColumn("Station.Result", "结果", "D310", ["0", "1", "1", "0"]));
        section.Columns.Add(CreateMatrixColumn("Station.Code", "代码", "D320", ["100", "101", "102", "103"]));
        section.RebuildRows();
        ArraySections.Add(section);
    }

    private static IoSignalModel CreateMatrixColumn(string key, string name, string address, IReadOnlyList<string> values)
    {
        var signal = new IoSignalModel
        {
            SignalKey = key,
            SignalName = name,
            PlcAddress = address,
            AddressCount = values.Count,
            Direction = "Read"
        };

        for (var index = 0; index < values.Count; index++)
        {
            signal.ExpandedValues.Add(new IoSignalValueModel(index + 1, values[index]));
        }

        return signal;
    }

    private async Task ManualReadSelectedDataAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var result = await _interactionPort.ReadAsync(SelectedDevice, CancellationToken.None);
        if (result.ShouldRefreshPreview)
        {
            RefreshPreviewValues();
            FeedbackMessage = Text("Navigation_Io_ManualReadPreviewUpdated", "已刷新页面预览值，未连接真实 PLC。");
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            FeedbackMessage = result.ErrorMessage;
        }
    }

    private async Task WriteInteractionRowAsync(IoInteractionRowModel row)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var result = await _interactionPort.WriteAsync(SelectedDevice, row, row.WriteValue, CancellationToken.None);
        if (!result.Accepted)
        {
            FeedbackMessage = result.ErrorMessage ?? Text("Navigation_Io_WriteRejected", "写入请求未被安全端口接受。");
            return;
        }

        foreach (var signal in row.HostSignals)
        {
            signal.SetValue(row.WriteValue);
        }

        row.NotifyValuesChanged();
        FeedbackMessage = Text("Navigation_Io_WritePreviewUpdated", "已更新写入预览值，未连接真实 PLC。");
    }

    private void RefreshPreviewValues()
    {
        _readVersion++;
        var value = _readVersion;

        foreach (var row in InteractionRows)
        {
            foreach (var signal in row.PlcSignals)
            {
                signal.SetValue(value);
            }

            foreach (var signal in row.HostSignals)
            {
                signal.SetValue(signal.Value);
            }

            row.InitializeWriteValueFromCurrentBuffer();
            row.NotifyValuesChanged();
        }

        foreach (var signal in DataSections.SelectMany(static section => section.Signals))
        {
            signal.SetValue(value * 10 + signal.SortOrder);
        }

        foreach (var section in ArraySections)
        {
            foreach (var column in section.Columns)
            {
                column.SetValue(value);
            }

            section.RebuildRows();
        }
    }

    private void NotifySignalCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasInteractionRows));
        OnPropertyChanged(nameof(HasDataSections));
        OnPropertyChanged(nameof(HasArraySections));
        OnPropertyChanged(nameof(HasNoSignals));
    }

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
