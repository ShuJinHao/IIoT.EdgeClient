using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

public sealed class IoViewViewModel : NavigationPageViewModelBase
{
    private readonly IHardwareConfigCrudService _hardwareConfigService;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IPlcDataStore _plcDataStore;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IIoViewSafeInteractionPort _safeInteractionPort;
    private readonly IClientPermissionService _permissionService;
    private readonly IAvaloniaRuntimeState _runtimeState;
    private readonly AsyncRelayCommand _refreshDevicesCommand;
    private readonly AsyncRelayCommand _manualReadCommand;
    private IoNetworkDeviceModel? _selectedDevice;
    private bool _isConnected;
    private string _feedbackMessage = string.Empty;
    private string _snapshotSourceText = "未启动";
    private string _snapshotRefreshText = "--";
    private string _writeGateStatusText = string.Empty;

    public IoViewViewModel(
        IHardwareConfigCrudService hardwareConfigService,
        IPlcConnectionManager plcConnectionManager,
        IPlcDataStore plcDataStore,
        IAvaloniaLanguageService languageService,
        IIoViewSafeInteractionPort safeInteractionPort,
        IClientPermissionService permissionService,
        IAvaloniaRuntimeState? runtimeState = null,
        string viewId = "Hardware.IOView",
        string titleResourceKey = "Navigation_Title_IoInteract",
        string titleFallback = "I/O 交互")
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _hardwareConfigService = hardwareConfigService;
        _plcConnectionManager = plcConnectionManager;
        _plcDataStore = plcDataStore;
        _languageService = languageService;
        _safeInteractionPort = safeInteractionPort;
        _permissionService = permissionService;
        _runtimeState = runtimeState ?? new AvaloniaRuntimeState();
        _refreshDevicesCommand = new AsyncRelayCommand(LoadDevicesAsync);
        _manualReadCommand = new AsyncRelayCommand(ManualReadSelectedDataAsync, () => SelectedDevice is not null);
        _runtimeState.StateChanged += (_, _) => RefreshConnectionState();
        _permissionService.PermissionStateChanged += RefreshWriteGateState;
        RefreshWriteGateState();
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
                _ = LoadMappingsAsync();
                _manualReadCommand.NotifyCanExecuteChanged();
                RefreshWriteGateState();
                OnPropertyChanged(nameof(HasSelectedDevice));
            }
        }
    }

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                RefreshWriteGateState();
            }
        }
    }

    public string FeedbackMessage
    {
        get => _feedbackMessage;
        private set => SetProperty(ref _feedbackMessage, value);
    }

    public string SnapshotSourceText
    {
        get => _snapshotSourceText;
        private set => SetProperty(ref _snapshotSourceText, value);
    }

    public string SnapshotRefreshText
    {
        get => _snapshotRefreshText;
        private set => SetProperty(ref _snapshotRefreshText, value);
    }

    public string WriteGateStatusText
    {
        get => _writeGateStatusText;
        private set => SetProperty(ref _writeGateStatusText, value);
    }

    public IAsyncRelayCommand RefreshDevicesCommand => _refreshDevicesCommand;

    public IAsyncRelayCommand ManualReadCommand => _manualReadCommand;

    public override string ViewTitle
        => Text("Navigation_Title_IoInteract", "I/O 交互");

    public override async Task OnActivatedAsync()
    {
        await LoadDevicesAsync();
    }

    private async Task LoadDevicesAsync()
    {
        var selectedId = SelectedDevice?.Id;
        try
        {
            var config = await _hardwareConfigService.LoadAsync();
            Devices.Clear();
            foreach (var device in config.NetworkDevices
                .Where(static device => device.Id > 0)
                .OrderBy(static device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
                .Select(static device => new IoNetworkDeviceModel
                {
                    Id = device.Id,
                    DeviceName = device.DeviceName,
                    DeviceCode = string.IsNullOrWhiteSpace(device.ModuleId) ? device.DeviceModel ?? string.Empty : device.ModuleId
                }))
            {
                Devices.Add(device);
            }

            SelectedDevice = selectedId is null
                ? Devices.FirstOrDefault()
                : Devices.FirstOrDefault(device => device.Id == selectedId.Value) ?? Devices.FirstOrDefault();

            if (SelectedDevice is null)
            {
                ClearMappings();
                FeedbackMessage = Text("Navigation_Io_NoDevices", "当前没有可显示的网络设备。");
            }
        }
        catch (Exception ex)
        {
            ClearMappings();
            FeedbackMessage = Format("Navigation_Io_LoadDevicesFailed", "I/O 设备加载失败：{0}", ex.Message);
        }
    }

    private async Task LoadMappingsAsync()
    {
        ClearMappings();
        IsConnected = false;
        SnapshotSourceText = Text("Navigation_Io_Source_NotStarted", "未启动");
        SnapshotRefreshText = "--";

        if (SelectedDevice is null)
        {
            NotifySignalCollectionsChanged();
            return;
        }

        try
        {
            var mappings = await _hardwareConfigService.LoadIoMappingsAsync(SelectedDevice.Id);
            BuildMappings(mappings.Items);
            RefreshConnectionState();
            NotifySignalCollectionsChanged();
            FeedbackMessage = InteractionRows.Count + DataSections.Count + ArraySections.Count == 0
                ? Text("Navigation_Io_DeviceNotBound", "当前设备未绑定运行时任务或 I/O 映射。")
                : Text("Navigation_Io_ConfigLoaded", "已按真实硬件配置加载 I/O 映射，手动读取仅使用运行时快照。");
        }
        catch (Exception ex)
        {
            FeedbackMessage = Format("Navigation_Io_LoadMappingsFailed", "I/O 映射加载失败：{0}", ex.Message);
        }
    }

    private void BuildMappings(IReadOnlyCollection<IoMappingVm> mappings)
    {
        var interactionGroups = mappings
            .Where(IsInteractionMapping)
            .GroupBy(static mapping => NormalizeGroup(mapping.BusinessGroup), StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Min(mapping => mapping.SortOrder));

        foreach (var group in interactionGroups)
        {
            var row = new IoInteractionRowModel
            {
                BusinessGroup = group.Key,
                SortOrder = group.Min(static mapping => mapping.SortOrder),
                ListSeparator = Text("Navigation_ListSeparator", "、")
            };

            foreach (var mapping in group.OrderBy(static mapping => mapping.SortOrder))
            {
                var signal = CreateSignal(mapping);
                if (IsWrite(mapping.Direction))
                {
                    row.AddHostSignal(signal);
                }
                else
                {
                    row.AddPlcSignal(signal);
                }
            }

            row.WriteCommand = new AsyncRelayCommand(() => WriteInteractionRowAsync(row), () => CanWriteInteraction(row));
            row.InitializeWriteValueFromCurrentBuffer();
            InteractionRows.Add(row);
        }

        var readMappings = mappings
            .Where(static mapping => !IsWrite(mapping.Direction))
            .Where(static mapping => !IsInteractionMapping(mapping))
            .OrderBy(static mapping => mapping.SortOrder)
            .ToArray();

        foreach (var group in readMappings.Where(static mapping => mapping.AddressCount <= 1)
            .GroupBy(static mapping => BuildSectionTitle(mapping), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var section = new IoDataSectionModel
            {
                Category = first.Category,
                BusinessGroup = NormalizeGroup(first.BusinessGroup),
                SortOrder = first.SortOrder,
                Title = group.Key,
                CanManualRead = true
            };
            foreach (var mapping in group)
            {
                section.Signals.Add(CreateSignal(mapping));
            }

            DataSections.Add(section);
        }

        foreach (var group in readMappings.Where(static mapping => mapping.AddressCount > 1)
            .GroupBy(static mapping => BuildSectionTitle(mapping), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var section = new IoContinuousReadMatrixSectionModel(
                Text("Navigation_Io_CollapseDetails", "收起明细"),
                Text("Navigation_Io_ViewDetails", "查看明细"))
            {
                Category = first.Category,
                BusinessGroup = NormalizeGroup(first.BusinessGroup),
                SortOrder = first.SortOrder,
                Title = group.Key,
                EmptySummary = Text("Navigation_Io_NoContinuousValues", "暂无连续值"),
                SummaryFormat = Text("Navigation_Io_ArraySummaryFormat", "{0} 行 x {1} 项")
            };

            foreach (var mapping in group)
            {
                section.Columns.Add(CreateSignal(mapping));
            }

            section.RebuildRows();
            ArraySections.Add(section);
        }
    }

    private async Task ManualReadSelectedDataAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        if (!_runtimeState.IsRuntimeStarted)
        {
            FeedbackMessage = Text("Navigation_Io_RuntimeNotStarted", "运行链路未启动，无法读取运行时快照。");
            SnapshotSourceText = Text("Navigation_Io_Source_NotStarted", "未启动");
            SnapshotRefreshText = "--";
            IsConnected = false;
            return;
        }

        if (InteractionRows.Count + DataSections.Count + ArraySections.Count == 0)
        {
            FeedbackMessage = Text("Navigation_Io_DeviceNotBound", "当前设备未绑定运行时任务或 I/O 映射。");
            SnapshotSourceText = Text("Navigation_Io_Source_Unbound", "设备未绑定");
            SnapshotRefreshText = "--";
            RefreshConnectionState();
            return;
        }

        var buffer = _plcDataStore.GetBuffer(SelectedDevice.Id);
        if (buffer is null)
        {
            FeedbackMessage = Text("Navigation_Io_NoRuntimeSnapshot", "运行链路已启动，但当前设备暂无运行时快照。");
            SnapshotSourceText = Text("Navigation_Io_Source_NoSnapshot", "无快照");
            SnapshotRefreshText = "--";
            RefreshConnectionState();
            return;
        }

        ApplySnapshot(buffer);
        RefreshConnectionState();
        SnapshotSourceText = Format("Navigation_Io_Source_RuntimeSnapshot", "运行时快照 / {0}", SelectedDevice.DeviceName);
        SnapshotRefreshText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        FeedbackMessage = Text("Navigation_Io_ManualReadSnapshotUpdated", "已从运行时快照刷新页面值，未访问真实 PLC。");
        await Task.CompletedTask;
    }

    private async Task WriteInteractionRowAsync(IoInteractionRowModel row)
    {
        if (SelectedDevice is null)
        {
            FeedbackMessage = Text("Navigation_Io_Write_DeviceNotBound", "当前设备未绑定运行时设备，不能申请 I/O 写入。");
            return;
        }

        var result = await _safeInteractionPort.WriteAsync(SelectedDevice, row, row.WriteValue, CancellationToken.None);
        FeedbackMessage = result.Message;
        row.LastWriteResultText = result.Message;
        if (result.Accepted)
        {
            row.LastWriteValueText = row.WriteValue.ToString();
        }

        RefreshConnectionState();
        RefreshWriteGateState();
    }

    private void ApplySnapshot(IPlcBuffer buffer)
    {
        foreach (var row in InteractionRows)
        {
            foreach (var signal in row.PlcSignals)
            {
                ApplySignalSnapshot(buffer, signal, readDirection: true);
            }

            foreach (var signal in row.HostSignals)
            {
                ApplySignalSnapshot(buffer, signal, readDirection: false);
            }

            row.InitializeWriteValueFromCurrentBuffer();
            row.NotifyValuesChanged();
        }

        foreach (var signal in DataSections.SelectMany(static section => section.Signals))
        {
            ApplySignalSnapshot(buffer, signal, readDirection: true);
        }

        foreach (var section in ArraySections)
        {
            foreach (var column in section.Columns)
            {
                ApplySignalSnapshot(buffer, column, readDirection: true);
            }

            section.RebuildRows();
        }
    }

    private static void ApplySignalSnapshot(IPlcBuffer buffer, IoSignalModel signal, bool readDirection)
    {
        ushort[] words;
        var found = readDirection
            ? buffer.TryGetReadWords(signal.SignalKey, out words)
            : buffer.TryGetWriteWords(signal.SignalKey, out words);

        if (!found)
        {
            signal.DisplayValue = "-";
            signal.PreviewValue = "-";
            return;
        }

        if (signal.AddressCount > 1)
        {
            signal.ExpandedValues.Clear();
            for (var index = 0; index < Math.Min(signal.AddressCount, words.Length); index++)
            {
                signal.ExpandedValues.Add(new IoSignalValueModel(index + 1, words[index].ToString()));
            }
        }

        signal.SetValue(words.Length == 0 ? 0 : words[0]);
        if (words.Length > 1 && signal.AddressCount <= 1)
        {
            signal.DisplayValue = string.Join(",", words);
            signal.PreviewValue = signal.DisplayValue;
        }
    }

    private void RefreshConnectionState()
    {
        if (SelectedDevice is null)
        {
            IsConnected = false;
            RefreshWriteGateState();
            return;
        }

        IsConnected = _plcConnectionManager.GetRuntimeStatus(SelectedDevice.Id)?.IsConnected == true;
        RefreshWriteGateState();
    }

    private bool CanWriteInteraction(IoInteractionRowModel row)
        => row.CanWrite
            && SelectedDevice is not null
            && _runtimeState.IsRuntimeStarted
            && _permissionService.CanEditHardware
            && IsConnected;

    private void RefreshWriteGateState()
    {
        WriteGateStatusText = BuildWriteGateStatusText();
        foreach (var row in InteractionRows)
        {
            row.WriteCommand?.NotifyCanExecuteChanged();
        }
    }

    private string BuildWriteGateStatusText()
    {
        if (!_runtimeState.IsRuntimeStarted)
        {
            return Text("Navigation_Io_WriteGate_UiOnly", "写入闸门：UI-only，运行链路未启动。");
        }

        if (!_permissionService.CanEditHardware)
        {
            return Text("Navigation_Io_WriteGate_NoPermission", "写入闸门：当前用户无硬件配置权限。");
        }

        if (SelectedDevice is null)
        {
            return Text("Navigation_Io_WriteGate_NoDevice", "写入闸门：未选择网络设备。");
        }

        if (InteractionRows.Count == 0)
        {
            return Text("Navigation_Io_WriteGate_NoInteraction", "写入闸门：当前设备没有可写交互点位。");
        }

        if (!IsConnected)
        {
            return Text("Navigation_Io_WriteGate_PlcDisconnected", "写入闸门：PLC 未连接。");
        }

        return Text("Navigation_Io_WriteGate_Ready", "写入闸门：运行中，可申请写入运行时缓冲。");
    }

    private void ClearMappings()
    {
        InteractionRows.Clear();
        DataSections.Clear();
        ArraySections.Clear();
        NotifySignalCollectionsChanged();
        RefreshWriteGateState();
    }

    private void NotifySignalCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasInteractionRows));
        OnPropertyChanged(nameof(HasDataSections));
        OnPropertyChanged(nameof(HasArraySections));
        OnPropertyChanged(nameof(HasNoSignals));
    }

    private IoSignalModel CreateSignal(IoMappingVm mapping)
        => new()
        {
            SignalKey = mapping.SignalKey,
            SignalName = string.IsNullOrWhiteSpace(mapping.SignalName) ? mapping.SignalKey : mapping.SignalName,
            PlcAddress = mapping.PlcAddress,
            AddressCount = Math.Max(1, mapping.AddressCount),
            Direction = mapping.Direction,
            DataType = mapping.DataType,
            DirectionText = IsWrite(mapping.Direction)
                ? Text("Navigation_Io_Direction_HostToPlc", "上位机到 PLC")
                : Text("Navigation_Io_Direction_PlcToHost", "PLC 到上位机"),
            Remark = mapping.Remark,
            SortOrder = mapping.SortOrder
        };

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private string Format(string key, string fallback, params object[] args)
        => string.Format(Text(key, fallback), args);

    private static string BuildSectionTitle(IoMappingVm mapping)
    {
        var category = string.IsNullOrWhiteSpace(mapping.Category) ? "I/O" : mapping.Category.Trim();
        var group = NormalizeGroup(mapping.BusinessGroup);
        return string.IsNullOrWhiteSpace(group) ? category : $"{category} / {group}";
    }

    private static string NormalizeGroup(string? value)
        => string.IsNullOrWhiteSpace(value) ? "未分组" : value.Trim();

    private static bool IsInteractionMapping(IoMappingVm mapping)
        => Contains(mapping.Category, "Interaction")
            || Contains(mapping.Category, "交互")
            || Contains(mapping.BusinessGroup, "Interaction")
            || Contains(mapping.BusinessGroup, "交互");

    private static bool IsWrite(string? direction)
        => string.Equals(direction, "Write", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string token)
        => value?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
}
