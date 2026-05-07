using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public class HardwareConfigViewModel : LocalizedCrudPageViewModelBase
{
    private readonly IClientPermissionService _permissionService;
    private readonly IHardwareConfigLoadSaveCoordinator _loadSaveCoordinator;
    private readonly IHardwareConfigDeviceSelectionCoordinator _deviceSelectionCoordinator;
    private readonly IHardwareConfigEditSession _editSession;
    private readonly AsyncCommand _applyModuleTemplateCommand;
    private readonly BaseCommand _addNetworkDeviceCommand;
    private readonly BaseCommand _deleteNetworkDeviceCommand;
    private readonly BaseCommand _addSerialDeviceCommand;
    private readonly BaseCommand _deleteSerialDeviceCommand;
    private readonly BaseCommand _openAddInteractionMappingDialogCommand;
    private readonly BaseCommand _openAddDataPointMappingDialogCommand;
    private readonly BaseCommand _confirmAddIoMappingCommand;
    private readonly BaseCommand _cancelAddIoMappingDialogCommand;
    private readonly BaseCommand _deleteIoMappingCommand;
    private readonly AsyncCommand _saveCommand;
    private bool _hasModuleTemplate;

    public IEnumerable<DeviceType> DeviceTypes => Enum.GetValues<DeviceType>();
    public IEnumerable<PlcType> PlcTypes => Enum.GetValues<PlcType>();

    public bool CanEdit => _permissionService.CanEditHardware;

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public ObservableCollection<NetworkDeviceVm> NetworkDevices { get; } = new();
    public ObservableCollection<SerialDeviceVm> SerialDevices { get; } = new();
    public ObservableCollection<IoMappingVm> IoMappings { get; } = new();
    public ICollectionView IoMappingsView { get; }

    public IReadOnlyList<string> IoCategories => IoMappingOptionCatalog.Categories;
    public IReadOnlyList<string> IoDataPointCategories => IoMappingOptionCatalog.DataPointCategories;
    public IReadOnlyList<string> IoDirections => IoMappingOptionCatalog.Directions;
    public IReadOnlyList<string> IoDataTypes => IoMappingOptionCatalog.DataTypes;
    public IReadOnlyList<string> IoPointSources => IoMappingOptionCatalog.PointSources;

    public ObservableCollection<IoStandardSignalOptionVm> StandardIoSignals { get; } = new();
    public ObservableCollection<IoStandardSignalOptionVm> StandardDataSignals { get; } = new();
    public ObservableCollection<IoStandardSignalOptionVm> FilteredStandardDataSignals { get; } = new();
    public ObservableCollection<IoStandardSignalGroupOptionVm> StandardInteractionGroups { get; } = new();

    private IoStandardSignalOptionVm? _selectedStandardIoSignal;
    public IoStandardSignalOptionVm? SelectedStandardIoSignal
    {
        get => _selectedStandardIoSignal;
        set
        {
            if (ReferenceEquals(_selectedStandardIoSignal, value))
            {
                return;
            }

            _selectedStandardIoSignal = value;
            OnPropertyChanged();
            _editSession.ApplyStandardSignalToDraft(this, value);
        }
    }

    private IoStandardSignalGroupOptionVm? _selectedStandardInteractionGroup;
    public IoStandardSignalGroupOptionVm? SelectedStandardInteractionGroup
    {
        get => _selectedStandardInteractionGroup;
        set
        {
            if (ReferenceEquals(_selectedStandardInteractionGroup, value))
            {
                return;
            }

            _selectedStandardInteractionGroup = value;
            OnPropertyChanged();
            _editSession.ApplyStandardInteractionGroupToDraft(this, value);
        }
    }

    private string _moduleTemplateHint = "请选择 PLC 设备后导入插件标准点位。";
    public string ModuleTemplateHint
    {
        get => _moduleTemplateHint;
        internal set
        {
            _moduleTemplateHint = value;
            OnPropertyChanged();
        }
    }

    private bool _isAddIoMappingDialogOpen;
    public bool IsAddIoMappingDialogOpen
    {
        get => _isAddIoMappingDialogOpen;
        internal set
        {
            _isAddIoMappingDialogOpen = value;
            OnPropertyChanged();
        }
    }

    private bool _isInteractionPairDialog;
    public bool IsInteractionPairDialog
    {
        get => _isInteractionPairDialog;
        internal set
        {
            _isInteractionPairDialog = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDataPointDialog));
        }
    }

    public bool IsDataPointDialog => !IsInteractionPairDialog;

    private IoMappingDraftVm? _newIoMapping;
    public IoMappingDraftVm? NewIoMapping
    {
        get => _newIoMapping;
        internal set
        {
            if (_newIoMapping is not null)
            {
                _newIoMapping.PropertyChanged -= OnNewIoMappingPropertyChanged;
            }

            _newIoMapping = value;
            if (_newIoMapping is not null)
            {
                _newIoMapping.PropertyChanged += OnNewIoMappingPropertyChanged;
            }

            OnPropertyChanged();
        }
    }

    private IoInteractionPairDraftVm? _newInteractionPair;
    public IoInteractionPairDraftVm? NewInteractionPair
    {
        get => _newInteractionPair;
        internal set
        {
            if (_newInteractionPair is not null)
            {
                _newInteractionPair.PropertyChanged -= OnNewInteractionPairPropertyChanged;
            }

            _newInteractionPair = value;
            if (_newInteractionPair is not null)
            {
                _newInteractionPair.PropertyChanged += OnNewInteractionPairPropertyChanged;
            }

            OnPropertyChanged();
        }
    }

    private NetworkDeviceVm? _selectedNetworkDevice;
    public NetworkDeviceVm? SelectedNetworkDevice
    {
        get => _selectedNetworkDevice;
        set
        {
            if (ReferenceEquals(_selectedNetworkDevice, value))
            {
                return;
            }

            if (_selectedNetworkDevice is not null)
            {
                _selectedNetworkDevice.PropertyChanged -= OnSelectedNetworkDevicePropertyChanged;
            }

            _selectedNetworkDevice = value;
            if (_selectedNetworkDevice is not null)
            {
                _selectedNetworkDevice.PropertyChanged += OnSelectedNetworkDevicePropertyChanged;
            }

            OnPropertyChanged();
            _deviceSelectionCoordinator.HandleSelectedNetworkDeviceChanged(this);
            _ = RefreshSelectedNetworkDeviceAsync();
        }
    }

    private IoMappingVm? _selectedIoMapping;
    public IoMappingVm? SelectedIoMapping
    {
        get => _selectedIoMapping;
        set
        {
            if (ReferenceEquals(_selectedIoMapping, value))
            {
                return;
            }

            _selectedIoMapping = value;
            OnPropertyChanged();
            _deleteIoMappingCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanApplyModuleTemplate =>
        CanEdit
        && SelectedNetworkDevice is not null
        && SelectedNetworkDevice.DeviceType == DeviceType.PLC
        && SelectedNetworkDevice.Id > 0
        && _hasModuleTemplate;

    public ICommand AddNetworkDeviceCommand { get; }
    public ICommand DeleteNetworkDeviceCommand { get; }
    public ICommand AddSerialDeviceCommand { get; }
    public ICommand DeleteSerialDeviceCommand { get; }
    public ICommand OpenAddInteractionMappingDialogCommand { get; }
    public ICommand OpenAddDataPointMappingDialogCommand { get; }
    public ICommand ConfirmAddIoMappingCommand { get; }
    public ICommand CancelAddIoMappingDialogCommand { get; }
    public ICommand DeleteIoMappingCommand { get; }
    public ICommand ApplyModuleTemplateCommand => _applyModuleTemplateCommand;
    public ICommand SaveCommand { get; }

    public HardwareConfigViewModel(
        IClientPermissionService permissionService,
        IAppLanguageService languageService,
        IHardwareConfigLoadSaveCoordinator loadSaveCoordinator,
        IHardwareConfigDeviceSelectionCoordinator deviceSelectionCoordinator,
        IHardwareConfigEditSession editSession)
        : this(
            permissionService,
            languageService,
            loadSaveCoordinator,
            deviceSelectionCoordinator,
            editSession,
            "Hardware.HardwareConfigView",
            "Navigation_Title_HardwareConfig",
            "硬件配置")
    {
    }

    public HardwareConfigViewModel(
        IClientPermissionService permissionService,
        IAppLanguageService languageService,
        IHardwareConfigLoadSaveCoordinator loadSaveCoordinator,
        IHardwareConfigDeviceSelectionCoordinator deviceSelectionCoordinator,
        IHardwareConfigEditSession editSession,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _permissionService = permissionService;
        _loadSaveCoordinator = loadSaveCoordinator;
        _deviceSelectionCoordinator = deviceSelectionCoordinator;
        _editSession = editSession;
        IoMappingsView = CollectionViewSource.GetDefaultView(IoMappings);
        IoMappingsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IoMappingVm.GroupTitle)));
        IoMappingsView.SortDescriptions.Add(new SortDescription(nameof(IoMappingVm.SortOrder), ListSortDirection.Ascending));

        _addNetworkDeviceCommand = (BaseCommand)CreateAddCommand(
            NetworkDevices,
            () => new NetworkDeviceVm { DeviceType = DeviceType.PLC, ModuleId = string.Empty },
            () => CanEdit);
        _deleteNetworkDeviceCommand = (BaseCommand)CreateDeleteCommand(NetworkDevices, () => CanEdit);
        _addSerialDeviceCommand = (BaseCommand)CreateAddCommand(
            SerialDevices,
            () => new SerialDeviceVm(),
            () => CanEdit);
        _deleteSerialDeviceCommand = (BaseCommand)CreateDeleteCommand(SerialDevices, () => CanEdit);
        _openAddInteractionMappingDialogCommand = new BaseCommand(
            _ => _editSession.OpenAddInteractionMappingDialog(this),
            _ => CanEdit && SelectedNetworkDevice is not null);
        _openAddDataPointMappingDialogCommand = new BaseCommand(
            _ => _editSession.OpenAddDataPointMappingDialog(this),
            _ => CanEdit && SelectedNetworkDevice is not null);
        _confirmAddIoMappingCommand = new BaseCommand(
            _ => _editSession.ConfirmAddIoMapping(this),
            _ => CanEdit && IsAddIoMappingDialogOpen && (NewIoMapping is not null || NewInteractionPair is not null));
        _cancelAddIoMappingDialogCommand = new BaseCommand(_ => _editSession.CloseAddIoMappingDialog(this));
        _deleteIoMappingCommand = new BaseCommand(
            _ => _editSession.DeleteSelectedIoMapping(this),
            _ => CanEdit && SelectedIoMapping is not null);
        _applyModuleTemplateCommand = (AsyncCommand)CreateBusyCommand(
            ApplyModuleTemplateAsync,
            () => CanApplyModuleTemplate);
        _saveCommand = (AsyncCommand)CreateBusyCommand(SaveAsync, () => CanEdit);

        AddNetworkDeviceCommand = _addNetworkDeviceCommand;
        DeleteNetworkDeviceCommand = _deleteNetworkDeviceCommand;
        AddSerialDeviceCommand = _addSerialDeviceCommand;
        DeleteSerialDeviceCommand = _deleteSerialDeviceCommand;
        OpenAddInteractionMappingDialogCommand = _openAddInteractionMappingDialogCommand;
        OpenAddDataPointMappingDialogCommand = _openAddDataPointMappingDialogCommand;
        ConfirmAddIoMappingCommand = _confirmAddIoMappingCommand;
        CancelAddIoMappingDialogCommand = _cancelAddIoMappingDialogCommand;
        DeleteIoMappingCommand = _deleteIoMappingCommand;
        SaveCommand = _saveCommand;

        _permissionService.PermissionStateChanged += HandlePermissionStateChanged;
    }

    public override async Task OnActivatedAsync()
    {
        await ExecuteBusyAsync(LoadAllAsync);
    }

    private Task LoadAllAsync()
        => _loadSaveCoordinator.LoadAllAsync(this);

    private Task RefreshSelectedNetworkDeviceAsync()
        => _loadSaveCoordinator.RefreshSelectedNetworkDeviceAsync(this);

    internal Task RefreshModuleTemplateInfoAsync()
        => _loadSaveCoordinator.RefreshModuleTemplateInfoAsync(this);

    private Task<CrudOperationResult> ApplyModuleTemplateAsync()
        => _loadSaveCoordinator.ApplyModuleTemplateAsync(this);

    private Task<CrudOperationResult> SaveAsync()
        => _loadSaveCoordinator.SaveAsync(this);

    private void OnSelectedNetworkDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => _deviceSelectionCoordinator.HandleSelectedNetworkDevicePropertyChanged(this, e);

    private void OnNewIoMappingPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => _editSession.HandleNewIoMappingPropertyChanged(this, sender, e);

    private void OnNewInteractionPairPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => _editSession.HandleNewInteractionPairPropertyChanged(this, sender, e);

    private void HandlePermissionStateChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshPermissionState();
            return;
        }

        dispatcher.Invoke(RefreshPermissionState);
    }

    private void RefreshPermissionState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanApplyModuleTemplate));
        _addNetworkDeviceCommand.RaiseCanExecuteChanged();
        _deleteNetworkDeviceCommand.RaiseCanExecuteChanged();
        _addSerialDeviceCommand.RaiseCanExecuteChanged();
        _deleteSerialDeviceCommand.RaiseCanExecuteChanged();
        RefreshAddCommands();
        _confirmAddIoMappingCommand.RaiseCanExecuteChanged();
        _cancelAddIoMappingDialogCommand.RaiseCanExecuteChanged();
        _deleteIoMappingCommand.RaiseCanExecuteChanged();
        _applyModuleTemplateCommand.RaiseCanExecuteChanged();
        _saveCommand.RaiseCanExecuteChanged();
    }

    internal static void ReplaceCollection<TItem>(
        ObservableCollection<TItem> target,
        IEnumerable<TItem> items)
        => ReplaceItems(target, items);

    internal void RefreshAddCommands()
    {
        _openAddInteractionMappingDialogCommand.RaiseCanExecuteChanged();
        _openAddDataPointMappingDialogCommand.RaiseCanExecuteChanged();
    }

    internal void SetModuleTemplateAvailable(bool value)
    {
        _hasModuleTemplate = value;
        OnPropertyChanged(nameof(CanApplyModuleTemplate));
        _applyModuleTemplateCommand.RaiseCanExecuteChanged();
    }

    internal void RaiseConfirmAddIoMappingCanExecuteChanged()
        => _confirmAddIoMappingCommand.RaiseCanExecuteChanged();

    internal void RaiseDeleteIoMappingCanExecuteChanged()
        => _deleteIoMappingCommand.RaiseCanExecuteChanged();

    internal void ReportError(string message)
        => SetError(message);

    internal void ClearUserFeedback()
        => ClearFeedback();
}
