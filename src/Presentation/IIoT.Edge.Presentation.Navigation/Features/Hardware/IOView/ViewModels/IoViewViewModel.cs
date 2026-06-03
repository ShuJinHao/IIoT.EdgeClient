using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IOView;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.Enums;
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

    public ObservableCollection<NetworkDeviceEntity> Devices { get; } = [];

    public ObservableCollection<IoInteractionRowModel> InteractionRows { get; } = [];

    public ObservableCollection<IoDataSectionModel> DataSections { get; } = [];

    public ObservableCollection<IoContinuousReadMatrixSectionModel> ArraySections { get; } = [];

    public bool HasInteractionRows => InteractionRows.Count > 0;

    public bool HasDataSections => DataSections.Count > 0;

    public bool HasArraySections => ArraySections.Count > 0;

    public bool HasNoSignals => !HasInteractionRows && !HasDataSections && !HasArraySections && SelectedDevice is not null;

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
            OnPropertyChanged(nameof(ConnectionStateText));
            _manualReadCommand.RaiseCanExecuteChanged();
            _ = LoadMappingsAsync();
        }
    }

    public bool HasSelectedDevice => SelectedDevice is not null;

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
        IIoViewManualReadService manualReadService)
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
            manualReadService)
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
            manualReadService)
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
        IIoViewManualReadService manualReadService)
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

        RefreshDevicesCommand = new AsyncCommand(LoadDevicesAsync);
        _manualReadCommand = new AsyncCommand(ManualReadSelectedDataAsync, () => SelectedDevice is not null);
    }

    public async Task LoadDevicesAsync()
    {
        var selectedDeviceId = SelectedDevice?.Id;
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

        var nextSelected = selectedDeviceId is null
            ? Devices.FirstOrDefault()
            : Devices.FirstOrDefault(x => x.Id == selectedDeviceId.Value) ?? Devices.FirstOrDefault();

        if (SelectedDevice?.Id != nextSelected?.Id)
        {
            SelectedDevice = nextSelected;
            return;
        }

        await LoadMappingsAsync();
        UpdateConnectionStatus();
    }

    public async Task LoadMappingsAsync()
    {
        InteractionRows.Clear();
        DataSections.Clear();
        ArraySections.Clear();
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
            row.WriteCommand ??= new BaseCommand(_ => WriteInteractionRow(row), _ => row.CanWrite);
            InteractionRows.Add(row);
        }

        foreach (var section in mappedSignals.DataSections)
        {
            DataSections.Add(section);
        }

        foreach (var section in mappedSignals.ArraySections)
        {
            ArraySections.Add(section);
        }

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

        _signalValueUpdater.Refresh(InteractionRows, DataSections, ArraySections, buffer);
        UpdateConnectionStatus();
    }

    private async Task ManualReadSelectedDataAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var result = await _manualReadService.ReadAsync(SelectedDevice.Id, DataSections, ArraySections);
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

    private bool IsVisibleDevice(NetworkDeviceEntity device)
    {
        if (!device.IsEnabled || device.DeviceType != DeviceType.PLC)
        {
            return false;
        }

        return true;
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
        OnPropertyChanged(nameof(HasArraySections));
        OnPropertyChanged(nameof(HasNoSignals));
    }

    private void ApplyTextProvider(IoViewMappingBuildResult mappedSignals)
    {
        foreach (var row in mappedSignals.InteractionRows)
        {
            row.SetTextProvider(GetText);
        }

        foreach (var section in mappedSignals.DataSections)
        {
            foreach (var signal in section.Signals)
            {
                signal.SetTextProvider(GetText);
            }
        }

        foreach (var section in mappedSignals.ArraySections)
        {
            section.SetTextProvider(GetText);
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

        foreach (var section in ArraySections)
        {
            section.NotifyLocalizationChanged();
            foreach (var signal in section.Columns)
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
    }

    public override async Task OnActivatedAsync()
        => await LoadDevicesAsync();

    public override Task OnDeactivatedAsync()
    {
        UnbindSelectedBuffer();
        return Task.CompletedTask;
    }
}
