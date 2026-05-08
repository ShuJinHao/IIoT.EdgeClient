using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using MediatR;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public class IoViewViewModel : NavigationViewModelBase
{
    private readonly IPlcDataStore _dataStore;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly ISender _sender;
    private readonly AsyncCommand _manualReadCommand;
    private readonly IIoViewMappingBuilder _mappingBuilder;
    private readonly IIoViewSignalValueUpdater _signalValueUpdater;
    private readonly IIoViewBufferBindingCoordinator _bufferBindingCoordinator;
    private readonly IIoViewInteractionWriter _interactionWriter;
    private readonly IIoViewManualReadService _manualReadService;
    private readonly string? _moduleIdFilter;

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
        }
    }

    public ICommand RefreshDevicesCommand { get; }

    public ICommand ManualReadCommand => _manualReadCommand;

    public IoViewViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        ISender sender,
        IAppLanguageService languageService,
        IIoViewMappingBuilder mappingBuilder,
        IIoViewSignalValueUpdater signalValueUpdater,
        IIoViewBufferBindingCoordinator bufferBindingCoordinator,
        IIoViewInteractionWriter interactionWriter,
        IIoViewManualReadService manualReadService)
        : this(
            dataStore,
            plcConnectionManager,
            sender,
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
        ISender sender,
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
            sender,
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
        ISender sender,
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
        _sender = sender;
        _mappingBuilder = mappingBuilder;
        _signalValueUpdater = signalValueUpdater;
        _bufferBindingCoordinator = bufferBindingCoordinator;
        _interactionWriter = interactionWriter;
        _manualReadService = manualReadService;
        _moduleIdFilter = moduleIdFilter;

        RefreshDevicesCommand = new AsyncCommand(LoadDevicesAsync);
        _manualReadCommand = new AsyncCommand(ManualReadSelectedDataAsync, () => SelectedDevice is not null);
    }

    public async Task LoadDevicesAsync()
    {
        var selectedDeviceId = SelectedDevice?.Id;
        var result = await _sender.Send(new GetAllNetworkDevicesQuery());

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

        var result = await _sender.Send(new GetIoMappingsByDeviceQuery(SelectedDevice.Id, 0, int.MaxValue));
        if (!result.IsSuccess || result.Value is null)
        {
            BindSelectedBuffer();
            UpdateConnectionStatus();
            return;
        }

        var mappedSignals = _mappingBuilder.Build(result.Value.Items);
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

        return string.IsNullOrWhiteSpace(_moduleIdFilter)
            || string.Equals(device.ModuleId, _moduleIdFilter, StringComparison.OrdinalIgnoreCase);
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
    }

    public override async Task OnActivatedAsync()
        => await LoadDevicesAsync();

    public override Task OnDeactivatedAsync()
    {
        UnbindSelectedBuffer();
        return Task.CompletedTask;
    }
}
