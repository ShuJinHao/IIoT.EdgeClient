using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;
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
    private readonly BaseCommand _editNetworkDeviceCommand;
    private readonly BaseCommand _confirmNetworkDeviceDialogCommand;
    private readonly BaseCommand _cancelNetworkDeviceDialogCommand;
    private readonly BaseCommand _deleteNetworkDeviceCommand;
    private readonly BaseCommand _addSerialDeviceCommand;
    private readonly BaseCommand _editSerialDeviceCommand;
    private readonly BaseCommand _confirmSerialDeviceDialogCommand;
    private readonly BaseCommand _cancelSerialDeviceDialogCommand;
    private readonly BaseCommand _deleteSerialDeviceCommand;
    private readonly BaseCommand _openAddInteractionMappingDialogCommand;
    private readonly BaseCommand _openAddDataPointMappingDialogCommand;
    private readonly BaseCommand _openEditIoMappingDialogCommand;
    private readonly BaseCommand _confirmAddIoMappingCommand;
    private readonly BaseCommand _confirmEditIoMappingCommand;
    private readonly BaseCommand _cancelAddIoMappingDialogCommand;
    private readonly BaseCommand _cancelEditIoMappingDialogCommand;
    private readonly BaseCommand _deleteIoMappingCommand;
    private readonly AsyncCommand _saveCommand;
    private bool _hasModuleTemplate;
    private NetworkDeviceVm? _networkDeviceEditingSource;
    private SerialDeviceVm? _serialDeviceEditingSource;
    private IoMappingVm? _ioMappingEditingSource;
    private IoInteractionPairVm? _ioInteractionPairEditingSource;

    public IEnumerable<DeviceType> DeviceTypes => Enum.GetValues<DeviceType>();
    public IEnumerable<PlcType> PlcTypes => Enum.GetValues<PlcType>();
    public IReadOnlyList<string> StopBitOptions { get; } = ["One", "OnePointFive", "Two"];
    public IReadOnlyList<string> ParityOptions { get; } = ["None", "Odd", "Even"];

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
    public ObservableCollection<NetworkDeviceVm> IoMappingNetworkDevices { get; } = new();
    public ObservableCollection<IoMappingGroupVm> IoMappingGroups { get; } = new();
    public ObservableCollection<IoInteractionPairVm> InteractionIoMappingPairs { get; } = new();
    public ObservableCollection<IoMappingGroupVm> InteractionIoMappingGroups { get; } = new();
    public ObservableCollection<IoMappingGroupVm> SingleReadIoMappingGroups { get; } = new();
    public ObservableCollection<IoMappingGroupVm> ContinuousReadIoMappingGroups { get; } = new();
    public ObservableCollection<IoMappingGroupVm> SingleWriteIoMappingGroups { get; } = new();
    public ObservableCollection<IoMappingGroupVm> ContinuousWriteIoMappingGroups { get; } = new();
    public bool HasNoIoMappingNetworkDevices => IoMappingNetworkDevices.Count == 0;
    public bool HasNoIoMappingGroups => IoMappingGroups.Count == 0;
    public bool HasNoInteractionIoMappingGroups => InteractionIoMappingPairs.Count == 0;
    public bool HasNoSingleReadIoMappingGroups => SingleReadIoMappingGroups.Count == 0;
    public bool HasNoContinuousReadIoMappingGroups => ContinuousReadIoMappingGroups.Count == 0;
    public bool HasNoSingleWriteIoMappingGroups => SingleWriteIoMappingGroups.Count == 0;
    public bool HasNoContinuousWriteIoMappingGroups => ContinuousWriteIoMappingGroups.Count == 0;

    public IReadOnlyList<string> IoCategories => IoMappingOptionCatalog.Categories;
    public IReadOnlyList<string> IoDataPointCategories => IoMappingOptionCatalog.DataPointCategories;
    public IReadOnlyList<string> IoDirections => IoMappingOptionCatalog.Directions;
    public IReadOnlyList<string> IoDataTypes => IoMappingOptionCatalog.DataTypes;

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

    private bool _isEditIoMappingDialogOpen;
    public bool IsEditIoMappingDialogOpen
    {
        get => _isEditIoMappingDialogOpen;
        internal set
        {
            _isEditIoMappingDialogOpen = value;
            OnPropertyChanged();
        }
    }

    private bool _isNetworkDeviceDialogOpen;
    public bool IsNetworkDeviceDialogOpen
    {
        get => _isNetworkDeviceDialogOpen;
        internal set
        {
            _isNetworkDeviceDialogOpen = value;
            OnPropertyChanged();
        }
    }

    private bool _isNetworkDeviceEditMode;
    public bool IsNetworkDeviceEditMode
    {
        get => _isNetworkDeviceEditMode;
        internal set
        {
            _isNetworkDeviceEditMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NetworkDeviceDialogTitle));
        }
    }

    public string NetworkDeviceDialogTitle
        => IsNetworkDeviceEditMode
            ? GetText("Navigation_Dialog_EditNetworkDevice", "编辑网络设备")
            : GetText("Navigation_Dialog_AddNetworkDevice", "新增网络设备");

    private bool _isSerialDeviceDialogOpen;
    public bool IsSerialDeviceDialogOpen
    {
        get => _isSerialDeviceDialogOpen;
        internal set
        {
            _isSerialDeviceDialogOpen = value;
            OnPropertyChanged();
        }
    }

    private bool _isSerialDeviceEditMode;
    public bool IsSerialDeviceEditMode
    {
        get => _isSerialDeviceEditMode;
        internal set
        {
            _isSerialDeviceEditMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SerialDeviceDialogTitle));
        }
    }

    public string SerialDeviceDialogTitle
        => IsSerialDeviceEditMode
            ? GetText("Navigation_Dialog_EditSerialDevice", "编辑串口设备")
            : GetText("Navigation_Dialog_AddSerialDevice", "新增串口设备");

    public string IoMappingEditDialogTitle
        => IsEditingInteractionPair && EditingInteractionPair is not null
            ? FormatText(
                "Navigation_Dialog_EditIoInteractionFormat",
                "编辑信号交互 - {0}",
                EditingInteractionPair.BusinessGroup)
            : GetText("Navigation_Dialog_EditIoPoint", "编辑 IO 点位");

    public bool IsEditingInteractionPair => EditingInteractionPair is not null;

    public bool IsEditingSingleIoMapping => EditingIoMapping is not null;

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

    private NetworkDeviceVm? _editingNetworkDevice;
    public NetworkDeviceVm? EditingNetworkDevice
    {
        get => _editingNetworkDevice;
        internal set
        {
            _editingNetworkDevice = value;
            OnPropertyChanged();
        }
    }

    private SerialDeviceVm? _editingSerialDevice;
    public SerialDeviceVm? EditingSerialDevice
    {
        get => _editingSerialDevice;
        internal set
        {
            _editingSerialDevice = value;
            OnPropertyChanged();
        }
    }

    private IoMappingVm? _editingIoMapping;
    public IoMappingVm? EditingIoMapping
    {
        get => _editingIoMapping;
        internal set
        {
            _editingIoMapping = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditingSingleIoMapping));
            OnPropertyChanged(nameof(IoMappingEditDialogTitle));
        }
    }

    private IoInteractionPairDraftVm? _editingInteractionPair;
    public IoInteractionPairDraftVm? EditingInteractionPair
    {
        get => _editingInteractionPair;
        internal set
        {
            _editingInteractionPair = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditingInteractionPair));
            OnPropertyChanged(nameof(IoMappingEditDialogTitle));
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
            OnPropertyChanged(nameof(CanAddIoMappingForSelectedDevice));
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
            if (value is not null)
            {
                _selectedInteractionPair = null;
                OnPropertyChanged(nameof(SelectedInteractionPair));
            }

            OnPropertyChanged();
            _deleteIoMappingCommand.RaiseCanExecuteChanged();
            _openEditIoMappingDialogCommand.RaiseCanExecuteChanged();
        }
    }

    private IoInteractionPairVm? _selectedInteractionPair;
    public IoInteractionPairVm? SelectedInteractionPair
    {
        get => _selectedInteractionPair;
        set
        {
            if (ReferenceEquals(_selectedInteractionPair, value))
            {
                return;
            }

            _selectedInteractionPair = value;
            if (value is not null)
            {
                _selectedIoMapping = null;
                OnPropertyChanged(nameof(SelectedIoMapping));
            }

            OnPropertyChanged();
            _openEditIoMappingDialogCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanApplyModuleTemplate =>
        CanEdit
        && SelectedNetworkDevice is not null
        && SelectedNetworkDevice.DeviceType == DeviceType.PLC
        && SelectedNetworkDevice.Id > 0
        && _hasModuleTemplate;

    public bool CanAddIoMappingForSelectedDevice =>
        CanEdit
        && SelectedNetworkDevice is not null
        && SelectedNetworkDevice.DeviceType == DeviceType.PLC
        && SelectedNetworkDevice.Id > 0;

    public ICommand AddNetworkDeviceCommand { get; }
    public ICommand EditNetworkDeviceCommand { get; }
    public ICommand ConfirmNetworkDeviceDialogCommand { get; }
    public ICommand CancelNetworkDeviceDialogCommand { get; }
    public ICommand DeleteNetworkDeviceCommand { get; }
    public ICommand AddSerialDeviceCommand { get; }
    public ICommand EditSerialDeviceCommand { get; }
    public ICommand ConfirmSerialDeviceDialogCommand { get; }
    public ICommand CancelSerialDeviceDialogCommand { get; }
    public ICommand DeleteSerialDeviceCommand { get; }
    public ICommand OpenAddInteractionMappingDialogCommand { get; }
    public ICommand OpenAddDataPointMappingDialogCommand { get; }
    public ICommand OpenEditIoMappingDialogCommand { get; }
    public ICommand ConfirmAddIoMappingCommand { get; }
    public ICommand ConfirmEditIoMappingCommand { get; }
    public ICommand CancelAddIoMappingDialogCommand { get; }
    public ICommand CancelEditIoMappingDialogCommand { get; }
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
        _addNetworkDeviceCommand = new BaseCommand(_ => OpenAddNetworkDeviceDialog(), _ => CanEdit);
        _editNetworkDeviceCommand = new BaseCommand(
            OpenEditNetworkDeviceDialog,
            parameter => CanEdit && parameter is NetworkDeviceVm);
        _confirmNetworkDeviceDialogCommand = new BaseCommand(
            _ => ConfirmNetworkDeviceDialog(),
            _ => CanEdit && IsNetworkDeviceDialogOpen && EditingNetworkDevice is not null);
        _cancelNetworkDeviceDialogCommand = new BaseCommand(_ => CloseNetworkDeviceDialog());
        _deleteNetworkDeviceCommand = new BaseCommand(
            DeleteNetworkDevice,
            parameter => CanEdit && parameter is NetworkDeviceVm);
        _addSerialDeviceCommand = new BaseCommand(_ => OpenAddSerialDeviceDialog(), _ => CanEdit);
        _editSerialDeviceCommand = new BaseCommand(
            OpenEditSerialDeviceDialog,
            parameter => CanEdit && parameter is SerialDeviceVm);
        _confirmSerialDeviceDialogCommand = new BaseCommand(
            _ => ConfirmSerialDeviceDialog(),
            _ => CanEdit && IsSerialDeviceDialogOpen && EditingSerialDevice is not null);
        _cancelSerialDeviceDialogCommand = new BaseCommand(_ => CloseSerialDeviceDialog());
        _deleteSerialDeviceCommand = (BaseCommand)CreateDeleteCommand(SerialDevices, () => CanEdit);
        _openAddInteractionMappingDialogCommand = new BaseCommand(
            _ => _editSession.OpenAddInteractionMappingDialog(this),
            _ => CanAddIoMappingForSelectedDevice);
        _openAddDataPointMappingDialogCommand = new BaseCommand(
            _ => _editSession.OpenAddDataPointMappingDialog(this),
            _ => CanAddIoMappingForSelectedDevice);
        _openEditIoMappingDialogCommand = new BaseCommand(
            _ => OpenEditIoMappingDialog(),
            _ => CanEdit && (SelectedIoMapping is not null || SelectedInteractionPair is not null));
        _confirmAddIoMappingCommand = new BaseCommand(
            _ => _editSession.ConfirmAddIoMapping(this),
            _ => CanEdit && IsAddIoMappingDialogOpen && (NewIoMapping is not null || NewInteractionPair is not null));
        _confirmEditIoMappingCommand = new BaseCommand(
            _ => ConfirmEditIoMappingDialog(),
            _ => CanEdit && IsEditIoMappingDialogOpen && (EditingIoMapping is not null || EditingInteractionPair is not null));
        _cancelAddIoMappingDialogCommand = new BaseCommand(_ => _editSession.CloseAddIoMappingDialog(this));
        _cancelEditIoMappingDialogCommand = new BaseCommand(_ => CloseEditIoMappingDialog());
        _deleteIoMappingCommand = new BaseCommand(
            _ => _editSession.DeleteSelectedIoMapping(this),
            _ => CanEdit && SelectedIoMapping is not null);
        _applyModuleTemplateCommand = (AsyncCommand)CreateBusyCommand(
            ApplyModuleTemplateAsync,
            () => CanApplyModuleTemplate);
        _saveCommand = (AsyncCommand)CreateBusyCommand(SaveAsync, () => CanEdit);

        AddNetworkDeviceCommand = _addNetworkDeviceCommand;
        EditNetworkDeviceCommand = _editNetworkDeviceCommand;
        ConfirmNetworkDeviceDialogCommand = _confirmNetworkDeviceDialogCommand;
        CancelNetworkDeviceDialogCommand = _cancelNetworkDeviceDialogCommand;
        DeleteNetworkDeviceCommand = _deleteNetworkDeviceCommand;
        AddSerialDeviceCommand = _addSerialDeviceCommand;
        EditSerialDeviceCommand = _editSerialDeviceCommand;
        ConfirmSerialDeviceDialogCommand = _confirmSerialDeviceDialogCommand;
        CancelSerialDeviceDialogCommand = _cancelSerialDeviceDialogCommand;
        DeleteSerialDeviceCommand = _deleteSerialDeviceCommand;
        OpenAddInteractionMappingDialogCommand = _openAddInteractionMappingDialogCommand;
        OpenAddDataPointMappingDialogCommand = _openAddDataPointMappingDialogCommand;
        OpenEditIoMappingDialogCommand = _openEditIoMappingDialogCommand;
        ConfirmAddIoMappingCommand = _confirmAddIoMappingCommand;
        ConfirmEditIoMappingCommand = _confirmEditIoMappingCommand;
        CancelAddIoMappingDialogCommand = _cancelAddIoMappingDialogCommand;
        CancelEditIoMappingDialogCommand = _cancelEditIoMappingDialogCommand;
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
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            RefreshPermissionState();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPermissionState);
    }

    private void RefreshPermissionState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanApplyModuleTemplate));
        OnPropertyChanged(nameof(CanAddIoMappingForSelectedDevice));
        _addNetworkDeviceCommand.RaiseCanExecuteChanged();
        _editNetworkDeviceCommand.RaiseCanExecuteChanged();
        _confirmNetworkDeviceDialogCommand.RaiseCanExecuteChanged();
        _cancelNetworkDeviceDialogCommand.RaiseCanExecuteChanged();
        _deleteNetworkDeviceCommand.RaiseCanExecuteChanged();
        _addSerialDeviceCommand.RaiseCanExecuteChanged();
        _editSerialDeviceCommand.RaiseCanExecuteChanged();
        _confirmSerialDeviceDialogCommand.RaiseCanExecuteChanged();
        _cancelSerialDeviceDialogCommand.RaiseCanExecuteChanged();
        _deleteSerialDeviceCommand.RaiseCanExecuteChanged();
        RefreshAddCommands();
        _openEditIoMappingDialogCommand.RaiseCanExecuteChanged();
        _confirmAddIoMappingCommand.RaiseCanExecuteChanged();
        _confirmEditIoMappingCommand.RaiseCanExecuteChanged();
        _cancelAddIoMappingDialogCommand.RaiseCanExecuteChanged();
        _cancelEditIoMappingDialogCommand.RaiseCanExecuteChanged();
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

    internal void RefreshIoMappingGroups()
    {
        var groups = HardwareConfigIoMappingGrouper.Build(IoMappings);

        ReplaceCollection(IoMappingGroups, groups.AllGroups);
        ReplaceCollection(InteractionIoMappingPairs, groups.InteractionPairs);
        ReplaceCollection(InteractionIoMappingGroups, groups.InteractionGroups);
        ReplaceCollection(SingleReadIoMappingGroups, groups.SingleReadGroups);
        ReplaceCollection(ContinuousReadIoMappingGroups, groups.ContinuousReadGroups);
        ReplaceCollection(SingleWriteIoMappingGroups, groups.SingleWriteGroups);
        ReplaceCollection(ContinuousWriteIoMappingGroups, groups.ContinuousWriteGroups);
        OnPropertyChanged(nameof(HasNoIoMappingGroups));
        OnPropertyChanged(nameof(HasNoInteractionIoMappingGroups));
        OnPropertyChanged(nameof(HasNoSingleReadIoMappingGroups));
        OnPropertyChanged(nameof(HasNoContinuousReadIoMappingGroups));
        OnPropertyChanged(nameof(HasNoSingleWriteIoMappingGroups));
        OnPropertyChanged(nameof(HasNoContinuousWriteIoMappingGroups));
    }

    internal void RefreshIoMappingNetworkDevices()
    {
        var devices = NetworkDevices
            .Where(static x => x.DeviceType == DeviceType.PLC)
            .OrderBy(static x => x.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReplaceCollection(IoMappingNetworkDevices, devices);
        OnPropertyChanged(nameof(HasNoIoMappingNetworkDevices));
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        OnPropertyChanged(nameof(NetworkDeviceDialogTitle));
        OnPropertyChanged(nameof(SerialDeviceDialogTitle));
        OnPropertyChanged(nameof(IoMappingEditDialogTitle));
    }

    private void OpenAddNetworkDeviceDialog()
    {
        _networkDeviceEditingSource = null;
        IsNetworkDeviceEditMode = false;
        EditingNetworkDevice = new NetworkDeviceVm
        {
            DeviceType = DeviceType.PLC
        };
        IsNetworkDeviceDialogOpen = true;
        _confirmNetworkDeviceDialogCommand.RaiseCanExecuteChanged();
    }

    private void OpenEditNetworkDeviceDialog(object? parameter)
    {
        if (parameter is not NetworkDeviceVm selected)
        {
            return;
        }

        _networkDeviceEditingSource = selected;
        IsNetworkDeviceEditMode = true;
        EditingNetworkDevice = HardwareConfigDraftMapper.CloneNetworkDevice(selected);
        IsNetworkDeviceDialogOpen = true;
        _confirmNetworkDeviceDialogCommand.RaiseCanExecuteChanged();
    }

    private void ConfirmNetworkDeviceDialog()
    {
        if (EditingNetworkDevice is null)
        {
            return;
        }

        if (_networkDeviceEditingSource is null)
        {
            NetworkDevices.Add(HardwareConfigDraftMapper.CloneNetworkDevice(EditingNetworkDevice));
        }
        else
        {
            HardwareConfigDraftMapper.CopyNetworkDevice(EditingNetworkDevice, _networkDeviceEditingSource);
        }

        RefreshIoMappingDeviceSelection();
        CloseNetworkDeviceDialog();
        ClearUserFeedback();
    }

    private void CloseNetworkDeviceDialog()
    {
        IsNetworkDeviceDialogOpen = false;
        IsNetworkDeviceEditMode = false;
        EditingNetworkDevice = null;
        _networkDeviceEditingSource = null;
        _confirmNetworkDeviceDialogCommand.RaiseCanExecuteChanged();
    }

    private void DeleteNetworkDevice(object? parameter)
    {
        if (parameter is not NetworkDeviceVm selected)
        {
            return;
        }

        NetworkDevices.Remove(selected);
        RefreshIoMappingDeviceSelection();
    }

    private void RefreshIoMappingDeviceSelection()
    {
        RefreshIoMappingNetworkDevices();
        if (SelectedNetworkDevice is not null && IoMappingNetworkDevices.Contains(SelectedNetworkDevice))
        {
            OnPropertyChanged(nameof(CanAddIoMappingForSelectedDevice));
            RefreshAddCommands();
            return;
        }

        SelectedNetworkDevice = IoMappingNetworkDevices.FirstOrDefault();
    }

    private void OpenAddSerialDeviceDialog()
    {
        _serialDeviceEditingSource = null;
        IsSerialDeviceEditMode = false;
        EditingSerialDevice = new SerialDeviceVm();
        IsSerialDeviceDialogOpen = true;
        _confirmSerialDeviceDialogCommand.RaiseCanExecuteChanged();
    }

    private void OpenEditSerialDeviceDialog(object? parameter)
    {
        if (parameter is not SerialDeviceVm selected)
        {
            return;
        }

        _serialDeviceEditingSource = selected;
        IsSerialDeviceEditMode = true;
        EditingSerialDevice = HardwareConfigDraftMapper.CloneSerialDevice(selected);
        IsSerialDeviceDialogOpen = true;
        _confirmSerialDeviceDialogCommand.RaiseCanExecuteChanged();
    }

    private void ConfirmSerialDeviceDialog()
    {
        if (EditingSerialDevice is null)
        {
            return;
        }

        if (_serialDeviceEditingSource is null)
        {
            SerialDevices.Add(HardwareConfigDraftMapper.CloneSerialDevice(EditingSerialDevice));
        }
        else
        {
            HardwareConfigDraftMapper.CopySerialDevice(EditingSerialDevice, _serialDeviceEditingSource);
        }

        CloseSerialDeviceDialog();
        ClearUserFeedback();
    }

    private void CloseSerialDeviceDialog()
    {
        IsSerialDeviceDialogOpen = false;
        IsSerialDeviceEditMode = false;
        EditingSerialDevice = null;
        _serialDeviceEditingSource = null;
        _confirmSerialDeviceDialogCommand.RaiseCanExecuteChanged();
    }

    private void OpenEditIoMappingDialog()
    {
        if (SelectedInteractionPair is not null)
        {
            _ioInteractionPairEditingSource = SelectedInteractionPair;
            EditingInteractionPair = HardwareConfigDraftMapper.CloneInteractionPair(SelectedInteractionPair);
            EditingIoMapping = null;
            IsEditIoMappingDialogOpen = true;
            _confirmEditIoMappingCommand.RaiseCanExecuteChanged();
            return;
        }

        if (SelectedIoMapping is null)
        {
            return;
        }

        _ioMappingEditingSource = SelectedIoMapping;
        EditingIoMapping = HardwareConfigDraftMapper.CloneIoMapping(SelectedIoMapping);
        EditingInteractionPair = null;
        IsEditIoMappingDialogOpen = true;
        _confirmEditIoMappingCommand.RaiseCanExecuteChanged();
    }

    private void ConfirmEditIoMappingDialog()
    {
        if (EditingInteractionPair is not null && _ioInteractionPairEditingSource is not null)
        {
            var validationError = ValidateEditingInteractionPair(EditingInteractionPair);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                ReportError(validationError);
                return;
            }

            HardwareConfigDraftMapper.CopyInteractionPair(EditingInteractionPair, _ioInteractionPairEditingSource);
            RefreshIoMappingGroups();
            CloseEditIoMappingDialog();
            ClearUserFeedback();
            return;
        }

        if (EditingIoMapping is null || _ioMappingEditingSource is null)
        {
            return;
        }

        var ioValidationError = ValidateEditingIoMapping(EditingIoMapping);
        if (!string.IsNullOrWhiteSpace(ioValidationError))
        {
            ReportError(ioValidationError);
            return;
        }

        HardwareConfigDraftMapper.CopyIoMapping(EditingIoMapping, _ioMappingEditingSource);
        RefreshIoMappingGroups();
        CloseEditIoMappingDialog();
        ClearUserFeedback();
    }

    private void CloseEditIoMappingDialog()
    {
        IsEditIoMappingDialogOpen = false;
        EditingIoMapping = null;
        EditingInteractionPair = null;
        _ioMappingEditingSource = null;
        _ioInteractionPairEditingSource = null;
        _confirmEditIoMappingCommand.RaiseCanExecuteChanged();
    }

    private string? ValidateEditingIoMapping(IoMappingVm mapping)
        => HardwareConfigDraftValidator.ValidateIoMapping(mapping, GetText);

    private string? ValidateEditingInteractionPair(IoInteractionPairDraftVm pair)
        => HardwareConfigDraftValidator.ValidateInteractionPair(
            pair,
            _ioInteractionPairEditingSource?.ReadMapping is not null
                && _ioInteractionPairEditingSource.WriteMapping is not null,
            GetText);
}
