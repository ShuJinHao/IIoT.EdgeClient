using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IOView;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public class IoViewViewModel : NavigationViewModelBase
{
    private readonly IPlcDataStore _dataStore;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IIoViewQueryFacade _queryFacade;
    private readonly AsyncCommand _manualReadCommand;
    private readonly IIoViewMappingBuilder _mappingBuilder;
    private readonly IIoViewSignalValueUpdater _signalValueUpdater;
    private readonly IIoViewBufferBindingCoordinator _bufferBindingCoordinator;
    private readonly IIoViewInteractionWriter _interactionWriter;
    private readonly IIoViewManualReadService _manualReadService;
    private readonly IDeviceSelectionService _deviceSelectionService;
    private bool _isDeviceSelectionSubscribed;

    public ObservableCollection<NetworkDeviceEntity> Devices { get; } = [];

    public ObservableCollection<IoInteractionRowModel> InteractionRows { get; } = [];

    public ObservableCollection<IoDataSectionModel> DataSections { get; } = [];

    public ObservableCollection<IoDataSectionModel> SingleReadSections { get; } = [];

    public ObservableCollection<IoDataSectionModel> ContinuousReadSections { get; } = [];

    public ObservableCollection<IoDataSectionModel> SingleWriteSections { get; } = [];

    public ObservableCollection<IoDataSectionModel> ContinuousWriteSections { get; } = [];

    public bool HasInteractionRows => InteractionRows.Count > 0;

    public bool HasDataSections => DataSections.Count > 0;

    public bool HasSingleReadSections => SingleReadSections.Count > 0;

    public bool HasContinuousReadSections => ContinuousReadSections.Count > 0;

    public bool HasSingleWriteSections => SingleWriteSections.Count > 0;

    public bool HasContinuousWriteSections => ContinuousWriteSections.Count > 0;

    public bool HasNoSignals => !HasInteractionRows && !HasDataSections && SelectedDevice is not null;

    private NetworkDeviceEntity? _selectedDevice;
    public NetworkDeviceEntity? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (_selectedDevice?.Id == value?.Id)
            {
                return;
            }

            UnbindSelectedBuffer();
            _selectedDevice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedDevice));
            OnPropertyChanged(nameof(IsDeviceSelectionRequired));
            OnPropertyChanged(nameof(ShouldShowDeviceSelectionPrompt));
            OnPropertyChanged(nameof(SelectedDeviceDisplayName));
            OnPropertyChanged(nameof(HasNoSignals));
            OnPropertyChanged(nameof(ConnectionStateText));
            _manualReadCommand.RaiseCanExecuteChanged();

            _ = LoadMappingsAsync();
        }
    }

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool HasDevices => Devices.Count > 0;

    public bool IsDeviceSelectionRequired => SelectedDevice is null;

    public bool ShouldShowDeviceSelectionPrompt => HasDevices && IsDeviceSelectionRequired;

    public string SelectedDeviceDisplayName
        => SelectedDevice is null
            ? GetText("Navigation_DeviceSelection_AllOrSummary", "全部/汇总")
            : FormatPlcIdentity(SelectedDevice.PlcCode, SelectedDevice.DeviceName);

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionStateText));
            _manualReadCommand.RaiseCanExecuteChanged();
            RaiseInteractionWriteCanExecuteChanged();
        }
    }

    public string ConnectionStateText
    {
        get
        {
            if (SelectedDevice is null)
            {
                return GetText("Navigation_Status_NoDevice", "未选择设备");
            }

            return IsConnected
                ? GetText("Navigation_Status_Connected", "已连接")
                : GetText("Navigation_Status_Disconnected", "未连接");
        }
    }

    public ICommand RefreshDevicesCommand { get; }

    public ICommand ManualReadCommand => _manualReadCommand;

    public IoViewViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        IIoViewQueryFacade queryFacade,
        IAppLanguageService languageService,
        IIoViewMappingBuilder mappingBuilder,
        IIoViewSignalValueUpdater signalValueUpdater,
        IIoViewBufferBindingCoordinator bufferBindingCoordinator,
        IIoViewInteractionWriter interactionWriter,
        IIoViewManualReadService manualReadService,
        IDeviceSelectionService deviceSelectionService)
        : this(
            dataStore,
            plcConnectionManager,
            queryFacade,
            languageService,
            "Hardware.IOView",
            "Navigation_Title_IoInteract",
            "IO 交互",
            moduleIdFilter: null,
            mappingBuilder,
            signalValueUpdater,
            bufferBindingCoordinator,
            interactionWriter,
            manualReadService,
            deviceSelectionService)
    {
    }

    public IoViewViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        IIoViewQueryFacade queryFacade,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback,
        IIoViewMappingBuilder mappingBuilder,
        IIoViewSignalValueUpdater signalValueUpdater,
        IIoViewBufferBindingCoordinator bufferBindingCoordinator,
        IIoViewInteractionWriter interactionWriter,
        IIoViewManualReadService manualReadService,
        IDeviceSelectionService deviceSelectionService,
        string? moduleIdFilter = null)
        : this(
            dataStore,
            plcConnectionManager,
            queryFacade,
            languageService,
            viewId,
            titleResourceKey,
            titleFallback,
            moduleIdFilter,
            mappingBuilder,
            signalValueUpdater,
            bufferBindingCoordinator,
            interactionWriter,
            manualReadService,
            deviceSelectionService)
    {
    }

    private IoViewViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        IIoViewQueryFacade queryFacade,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback,
        string? moduleIdFilter,
        IIoViewMappingBuilder mappingBuilder,
        IIoViewSignalValueUpdater signalValueUpdater,
        IIoViewBufferBindingCoordinator bufferBindingCoordinator,
        IIoViewInteractionWriter interactionWriter,
        IIoViewManualReadService manualReadService,
        IDeviceSelectionService deviceSelectionService)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _dataStore = dataStore;
        _plcConnectionManager = plcConnectionManager;
        _queryFacade = queryFacade;
        _mappingBuilder = mappingBuilder;
        _signalValueUpdater = signalValueUpdater;
        _bufferBindingCoordinator = bufferBindingCoordinator;
        _interactionWriter = interactionWriter;
        _manualReadService = manualReadService;
        _deviceSelectionService = deviceSelectionService;

        RefreshDevicesCommand = new AsyncCommand(LoadDevicesAsync);
        _manualReadCommand = new AsyncCommand(ManualReadSelectedDataAsync, () => SelectedDevice is not null && IsConnected);
    }

    public async Task LoadDevicesAsync()
    {
        var result = await _queryFacade.GetNetworkDevicesAsync();

        Devices.Clear();
        if (result.IsSuccess && result.Value != null)
        {
            foreach (var device in result.Value
                         .Where(IsVisibleDevice)
                         .OrderBy(static x => x.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                Devices.Add(device);
            }
        }
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(ShouldShowDeviceSelectionPrompt));

        var nextSelected = ResolveDeviceFromSharedSelection();

        if (SelectedDevice?.Id != nextSelected?.Id)
        {
            ApplySelectedDeviceFromSharedSelection(nextSelected);
            return;
        }

        await LoadMappingsAsync();
        UpdateConnectionStatus();
    }

    public async Task LoadMappingsAsync()
    {
        InteractionRows.Clear();
        DataSections.Clear();
        SingleReadSections.Clear();
        ContinuousReadSections.Clear();
        SingleWriteSections.Clear();
        ContinuousWriteSections.Clear();
        NotifySignalCollectionsChanged();

        if (SelectedDevice is null)
        {
            UnbindSelectedBuffer();
            UpdateConnectionStatus();
            return;
        }

        var result = await _queryFacade.GetIoMappingsAsync(SelectedDevice.Id, 0, int.MaxValue);
        if (!result.IsSuccess || result.Value is null)
        {
            BindSelectedBuffer();
            UpdateConnectionStatus();
            return;
        }

        var mappedSignals = _mappingBuilder.Build(result.Value.Items);
        ApplyTextProvider(mappedSignals);
        foreach (var row in mappedSignals.InteractionRows)
        {
            row.WriteCommand ??= new BaseCommand(_ => WriteInteractionRow(row), _ => row.CanWrite && IsConnected);
            InteractionRows.Add(row);
        }

        foreach (var section in mappedSignals.SingleReadSections)
        {
            SingleReadSections.Add(section);
        }

        foreach (var section in mappedSignals.ContinuousReadSections)
        {
            ContinuousReadSections.Add(section);
        }

        foreach (var section in mappedSignals.SingleWriteSections)
        {
            SingleWriteSections.Add(section);
        }

        foreach (var section in mappedSignals.ContinuousWriteSections)
        {
            ContinuousWriteSections.Add(section);
        }

        RebuildDataSections();
        BindSelectedBuffer();
        RefreshCurrentValues();
        NotifySignalCollectionsChanged();
        UpdateConnectionStatus();
    }

    public void RefreshCurrentValues()
    {
        if (SelectedDevice is null)
        {
            UpdateConnectionStatus();
            return;
        }

        var buffer = _dataStore.GetBuffer(SelectedDevice.Id);
        if (buffer is null)
        {
            UpdateConnectionStatus();
            return;
        }

        _signalValueUpdater.Refresh(InteractionRows, DataSections, [], buffer);
        UpdateConnectionStatus();
    }

    private async Task ManualReadSelectedDataAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var result = await _manualReadService.ReadAsync(SelectedDevice.Id, DataSections, []);
        if (result.ShouldRefreshValues)
        {
            RefreshCurrentValues();
            ClearFeedback();
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            SetError(result.ErrorMessage);
        }

        UpdateConnectionStatus();
    }

    private static bool IsVisibleDevice(NetworkDeviceEntity device)
        => device.DeviceType == DeviceType.PLC;

    private NetworkDeviceEntity? ResolveDeviceFromSharedSelection()
    {
        var selectedKey = _deviceSelectionService.SelectedDeviceKey;
        if (string.Equals(
                selectedKey,
                IDeviceSelectionService.AllFilterKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
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

    private void ApplySelectedDeviceFromSharedSelection(NetworkDeviceEntity? device)
    {
        SelectedDevice = device;
    }

    private void OnSharedDeviceSelectionChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() => ApplySelectedDeviceFromSharedSelection(ResolveDeviceFromSharedSelection()));
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }

    private void WriteInteractionRow(IoInteractionRowModel row)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        _interactionWriter.Write(SelectedDevice.Id, row);
    }

    private void BindSelectedBuffer()
    {
        UnbindSelectedBuffer();
        if (SelectedDevice is null)
        {
            return;
        }

        _bufferBindingCoordinator.Bind(SelectedDevice.Id, RefreshCurrentValues);
    }

    private void UnbindSelectedBuffer()
        => _bufferBindingCoordinator.Unbind();

    private void UpdateConnectionStatus()
    {
        if (SelectedDevice is null)
        {
            IsConnected = false;
            return;
        }

        var isConnected = _plcConnectionManager.GetRuntimeStatus(SelectedDevice.Id)?.IsConnected == true;
        IsConnected = isConnected;
    }

    private void NotifySignalCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasInteractionRows));
        OnPropertyChanged(nameof(HasDataSections));
        OnPropertyChanged(nameof(HasSingleReadSections));
        OnPropertyChanged(nameof(HasContinuousReadSections));
        OnPropertyChanged(nameof(HasSingleWriteSections));
        OnPropertyChanged(nameof(HasContinuousWriteSections));
        OnPropertyChanged(nameof(HasNoSignals));
    }

    private void RaiseInteractionWriteCanExecuteChanged()
    {
        foreach (var row in InteractionRows)
        {
            (row.WriteCommand as BaseCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void ApplyTextProvider(IoViewMappingBuildResult mappedSignals)
    {
        foreach (var row in mappedSignals.InteractionRows)
        {
            row.SetTextProvider(GetText);
        }

        foreach (var section in EnumerateDataSections(mappedSignals))
        {
            ApplySectionTextProvider(section);
        }
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        foreach (var section in DataSections)
        {
            section.NotifyLocalizationChanged();
            foreach (var signal in section.Signals)
            {
                signal.NotifyLocalizationChanged();
            }
        }

        foreach (var row in InteractionRows)
        {
            row.NotifyLocalizationChanged();
            foreach (var signal in row.PlcSignals.Concat(row.HostSignals))
            {
                signal.NotifyLocalizationChanged();
            }
        }

        OnPropertyChanged(nameof(ConnectionStateText));
        OnPropertyChanged(nameof(SelectedDeviceDisplayName));
    }

    private void RebuildDataSections()
    {
        DataSections.Clear();
        foreach (var section in SingleReadSections
                     .Concat(ContinuousReadSections)
                     .Concat(SingleWriteSections)
                     .Concat(ContinuousWriteSections))
        {
            DataSections.Add(section);
        }
    }

    private static IEnumerable<IoDataSectionModel> EnumerateDataSections(IoViewMappingBuildResult mappedSignals)
        => mappedSignals.SingleReadSections
            .Concat(mappedSignals.ContinuousReadSections)
            .Concat(mappedSignals.SingleWriteSections)
            .Concat(mappedSignals.ContinuousWriteSections);

    private void ApplySectionTextProvider(IoDataSectionModel section)
    {
        foreach (var signal in section.Signals)
        {
            signal.SetTextProvider(GetText);
        }
    }

    public override async Task OnActivatedAsync()
    {
        SubscribeDeviceSelection();
        await LoadDevicesAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        UnsubscribeDeviceSelection();
        UnbindSelectedBuffer();
        return Task.CompletedTask;
    }
}
