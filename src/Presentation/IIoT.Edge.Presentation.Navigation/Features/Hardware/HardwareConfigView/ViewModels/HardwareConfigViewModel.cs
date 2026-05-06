using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public class HardwareConfigViewModel : LocalizedCrudPageViewModelBase
{
    private const int ManualSortOrderBase = 10000;

    private readonly IHardwareConfigCrudService _crudService;
    private readonly IClientPermissionService _permissionService;
    private readonly IEditorValidator<NetworkDeviceVm> _networkDeviceValidator;
    private readonly IEditorValidator<SerialDeviceVm> _serialDeviceValidator;
    private readonly IEditorValidator<IoMappingVm> _ioMappingValidator;
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
    public IReadOnlyList<string> IoDataPointCategories { get; } =
    [
        IoMappingOptionCatalog.CategorySingleRead,
        IoMappingOptionCatalog.CategoryContinuousRead
    ];
    public IReadOnlyList<string> IoDirections => IoMappingOptionCatalog.Directions;
    public IReadOnlyList<string> IoDataTypes => IoMappingOptionCatalog.DataTypes;
    public IReadOnlyList<string> IoPointSources => IoMappingOptionCatalog.PointSources;

    public ObservableCollection<IoStandardSignalOptionVm> StandardIoSignals { get; } = new();
    public ObservableCollection<IoStandardSignalOptionVm> StandardDataSignals { get; } = new();
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
            ApplyStandardSignalToDraft(value);
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
            ApplyStandardInteractionGroupToDraft(value);
        }
    }

    private string _moduleTemplateHint = "请选择 PLC 设备后导入插件标准点位。";
    public string ModuleTemplateHint
    {
        get => _moduleTemplateHint;
        private set
        {
            _moduleTemplateHint = value;
            OnPropertyChanged();
        }
    }

    private bool _isAddIoMappingDialogOpen;
    public bool IsAddIoMappingDialogOpen
    {
        get => _isAddIoMappingDialogOpen;
        private set
        {
            _isAddIoMappingDialogOpen = value;
            OnPropertyChanged();
        }
    }

    private bool _isInteractionPairDialog;
    public bool IsInteractionPairDialog
    {
        get => _isInteractionPairDialog;
        private set
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
        private set
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
        private set
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
            SetModuleTemplateAvailable(false);
            ReplaceItems(StandardIoSignals, Array.Empty<IoStandardSignalOptionVm>());
            ReplaceItems(StandardDataSignals, Array.Empty<IoStandardSignalOptionVm>());
            ReplaceItems(StandardInteractionGroups, Array.Empty<IoStandardSignalGroupOptionVm>());
            ModuleTemplateHint = "请选择 PLC 设备后导入插件标准点位。";
            RefreshAddCommands();
            _ = RefreshSelectedNetworkDeviceAsync();
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
        IHardwareConfigCrudService crudService,
        IClientPermissionService permissionService,
        IAppLanguageService languageService)
        : this(
            crudService,
            permissionService,
            languageService,
            "Hardware.HardwareConfigView",
            "Navigation_Title_HardwareConfig",
            "硬件配置")
    {
    }

    public HardwareConfigViewModel(
        IHardwareConfigCrudService crudService,
        IClientPermissionService permissionService,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _crudService = crudService;
        _permissionService = permissionService;
        _networkDeviceValidator = new NetworkDeviceValidator(GetText, FormatText);
        _serialDeviceValidator = new SerialDeviceValidator(GetText, FormatText);
        _ioMappingValidator = new IoMappingValidator(GetText, FormatText);
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
            _ => OpenAddInteractionMappingDialog(),
            _ => CanEdit && SelectedNetworkDevice is not null);
        _openAddDataPointMappingDialogCommand = new BaseCommand(
            _ => OpenAddDataPointMappingDialog(),
            _ => CanEdit && SelectedNetworkDevice is not null);
        _confirmAddIoMappingCommand = new BaseCommand(
            _ => ConfirmAddIoMapping(),
            _ => CanEdit && IsAddIoMappingDialogOpen && (NewIoMapping is not null || NewInteractionPair is not null));
        _cancelAddIoMappingDialogCommand = new BaseCommand(_ => CloseAddIoMappingDialog());
        _deleteIoMappingCommand = new BaseCommand(
            parameter => DeleteIoMapping(parameter as IoMappingVm),
            parameter => CanEdit && parameter is IoMappingVm);
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

    private async Task LoadAllAsync()
    {
        var result = await _crudService.LoadAsync();

        ReplaceItems(NetworkDevices, result.NetworkDevices);
        ReplaceItems(SerialDevices, result.SerialDevices);

        if (NetworkDevices.Count > 0)
        {
            SelectedNetworkDevice = NetworkDevices[0];
        }
        else
        {
            SetModuleTemplateAvailable(false);
            ReplaceItems(IoMappings, []);
        }
    }

    private async Task RefreshSelectedNetworkDeviceAsync()
    {
        await LoadIoMappingsAsync();
        await RefreshModuleTemplateInfoAsync();
    }

    private async Task LoadIoMappingsAsync()
    {
        if (SelectedNetworkDevice is null || SelectedNetworkDevice.Id <= 0)
        {
            ReplaceItems(IoMappings, []);
            IoMappingsView.Refresh();
            return;
        }

        var result = await _crudService.LoadIoMappingsAsync(SelectedNetworkDevice.Id);
        ReplaceItems(IoMappings, result.Items);
        IoMappingsView.Refresh();
    }

    private async Task RefreshModuleTemplateInfoAsync()
    {
        var result = await _crudService.GetModuleTemplateInfoAsync(SelectedNetworkDevice);
        var defaultSignals = result.DefaultSignals.ToArray();
        ReplaceItems(StandardIoSignals, defaultSignals.Select(static x => new IoStandardSignalOptionVm(x)));
        ReplaceItems(
            StandardDataSignals,
            defaultSignals
                .Where(static x => !string.Equals(
                    IoMappingOptionCatalog.NormalizeCategory(x.Category, x.AddressCount),
                    IoMappingOptionCatalog.CategoryInteraction,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static x => new IoStandardSignalOptionVm(x)));
        ReplaceItems(
            StandardInteractionGroups,
            defaultSignals
                .Where(static x => string.Equals(
                    IoMappingOptionCatalog.NormalizeCategory(x.Category, x.AddressCount),
                    IoMappingOptionCatalog.CategoryInteraction,
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(static x => string.IsNullOrWhiteSpace(x.BusinessGroup) ? x.SignalKey : x.BusinessGroup.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(static x => new IoStandardSignalGroupOptionVm(x.Key, x.ToArray()))
                .OrderBy(static x => x.BusinessGroup, StringComparer.OrdinalIgnoreCase));
        ModuleTemplateHint = result.Message;
        SetModuleTemplateAvailable(result.IsAvailable);
    }

    private async Task<CrudOperationResult> ApplyModuleTemplateAsync()
    {
        var result = await _crudService.ApplyModuleTemplateAsync(SelectedNetworkDevice);
        if (result.IsSuccess)
        {
            await LoadIoMappingsAsync();
            await RefreshModuleTemplateInfoAsync();
        }

        return result;
    }

    private async Task<CrudOperationResult> SaveAsync()
    {
        var issues = new List<ValidationIssue>();
        issues.AddRange(await ValidateAsync(NetworkDevices, _networkDeviceValidator));
        issues.AddRange(await ValidateAsync(SerialDevices, _serialDeviceValidator));
        issues.AddRange(await ValidateAsync(IoMappings, _ioMappingValidator));
        issues.AddRange(ValidateInteractionPairs());

        var validationResult = CreateValidationResult(issues);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        var mappingsToSave = BuildMappingsToSave();

        var saveResult = await _crudService.SaveAsync(
            NetworkDevices,
            SerialDevices,
            SelectedNetworkDevice?.Id ?? 0,
            mappingsToSave);

        if (saveResult.IsSuccess
            || saveResult.Message.StartsWith("配置已保存", StringComparison.Ordinal))
        {
            await LoadAllAsync();
        }

        return saveResult;
    }

    private IReadOnlyCollection<IoMappingVm> BuildMappingsToSave()
    {
        var result = new List<IoMappingVm>(IoMappings.Count);

        foreach (var standard in IoMappings.Where(static x => !IsManualSignal(x)))
        {
            result.Add(CloneIoMapping(standard));
        }

        var manualOrdered = IoMappings
            .Where(static x => IsManualSignal(x))
            .OrderBy(static x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionWrite, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(static x => x.SortOrder <= 0 ? int.MaxValue : x.SortOrder)
            .ThenBy(static x => x.SignalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < manualOrdered.Length; index++)
        {
            var clone = CloneIoMapping(manualOrdered[index]);
            clone.SortOrder = ManualSortOrderBase + index;
            result.Add(clone);
        }

        return result;
    }

    private static bool IsManualSignal(IoMappingVm mapping)
        => mapping.SignalKey?.StartsWith("Manual.", StringComparison.OrdinalIgnoreCase) ?? false;

    private void OnSelectedNetworkDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkDeviceVm.ModuleId)
            or nameof(NetworkDeviceVm.DeviceType)
            or nameof(NetworkDeviceVm.Id))
        {
            _ = RefreshModuleTemplateInfoAsync();
            RefreshAddCommands();
        }
    }

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

    private void RefreshAddCommands()
    {
        _openAddInteractionMappingDialogCommand.RaiseCanExecuteChanged();
        _openAddDataPointMappingDialogCommand.RaiseCanExecuteChanged();
    }

    private void SetModuleTemplateAvailable(bool value)
    {
        _hasModuleTemplate = value;
        OnPropertyChanged(nameof(CanApplyModuleTemplate));
        _applyModuleTemplateCommand.RaiseCanExecuteChanged();
    }

    private void OpenAddInteractionMappingDialog()
    {
        if (SelectedNetworkDevice is null)
        {
            SetError(GetText("Navigation_Hardware_Validation_SelectNetworkDeviceFirst", "请先选择一个 PLC 设备。"));
            return;
        }

        IsInteractionPairDialog = true;
        var standardGroup = StandardInteractionGroups.FirstOrDefault(static x => x.HasReadAndWrite);
        var draft = new IoInteractionPairDraftVm
        {
            Source = standardGroup is not null
                ? IoMappingOptionCatalog.PointSourceStandardSignal
                : IoMappingOptionCatalog.PointSourceCustomDebug
        };

        NewInteractionPair = draft;
        NewIoMapping = null;
        SelectedStandardIoSignal = null;
        SelectedStandardInteractionGroup = draft.IsStandardSource ? standardGroup : null;
        if (draft.IsCustomSource)
        {
            ApplyCustomInteractionDefaults(draft);
        }

        IsAddIoMappingDialogOpen = true;
        _confirmAddIoMappingCommand.RaiseCanExecuteChanged();
    }

    private void OpenAddDataPointMappingDialog()
    {
        if (SelectedNetworkDevice is null)
        {
            SetError(GetText("Navigation_Hardware_Validation_SelectNetworkDeviceFirst", "请先选择一个 PLC 设备。"));
            return;
        }

        IsInteractionPairDialog = false;
        var standardSignal = FindStandardDataSignal();
        var draft = new IoMappingDraftVm
        {
            Source = standardSignal is not null
                ? IoMappingOptionCatalog.PointSourceStandardSignal
                : IoMappingOptionCatalog.PointSourceCustomDebug
        };

        NewIoMapping = draft;
        NewInteractionPair = null;
        SelectedStandardInteractionGroup = null;
        SelectedStandardIoSignal = draft.IsStandardSource ? standardSignal : null;
        if (draft.IsCustomSource)
        {
            ApplyCustomDebugDefaults(draft, IoMappingOptionCatalog.CategorySingleRead);
        }

        IsAddIoMappingDialogOpen = true;
        _confirmAddIoMappingCommand.RaiseCanExecuteChanged();
    }

    private IoStandardSignalOptionVm? FindStandardDataSignal()
        => StandardDataSignals.FirstOrDefault();

    private void ConfirmAddIoMapping()
    {
        if (IsInteractionPairDialog)
        {
            ConfirmAddInteractionPair();
            return;
        }

        ConfirmAddDataPoint();
    }

    private void ConfirmAddInteractionPair()
    {
        if (SelectedNetworkDevice is null || NewInteractionPair is null)
        {
            return;
        }

        var validationError = ValidateInteractionPairDraft(NewInteractionPair);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            SetError(validationError);
            return;
        }

        if (NewInteractionPair.IsStandardSource)
        {
            var group = SelectedStandardInteractionGroup!;
            var missingSignals = group.Signals
                .Where(signal => IoMappings.All(x => !string.Equals(x.SignalKey, signal.SignalKey, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static x => x.SortOrder)
                .ToArray();

            if (missingSignals.Length == 0)
            {
                SetError(GetText("Navigation_Hardware_Validation_InteractionGroupExists", "该信号交互组已全部存在，不能重复添加。"));
                return;
            }

            foreach (var signal in missingSignals)
            {
                IoMappings.Add(CreateMappingFromTemplate(signal, signal.PlcAddress));
            }
        }
        else
        {
            var group = NewInteractionPair.BusinessGroup.Trim();
            var sortOrder = NextSortOrder();
            var keySuffix = Guid.NewGuid().ToString("N");
            IoMappings.Add(CreateManualMapping(
                $"Manual.{keySuffix}.Read",
                NewInteractionPair.ReadPlcAddress,
                NewInteractionPair.ReadAddressCount,
                NewInteractionPair.ReadDataType,
                IoMappingOptionCatalog.DirectionRead,
                IoMappingOptionCatalog.CategoryInteraction,
                group,
                NewInteractionPair.ReadSignalName,
                sortOrder,
                NewInteractionPair.Remark));
            IoMappings.Add(CreateManualMapping(
                $"Manual.{keySuffix}.Write",
                NewInteractionPair.WritePlcAddress,
                NewInteractionPair.WriteAddressCount,
                NewInteractionPair.WriteDataType,
                IoMappingOptionCatalog.DirectionWrite,
                IoMappingOptionCatalog.CategoryInteraction,
                group,
                NewInteractionPair.WriteSignalName,
                sortOrder + 1,
                NewInteractionPair.Remark));
        }

        IoMappingsView.Refresh();
        CloseAddIoMappingDialog();
        ClearFeedback();
    }

    private void ConfirmAddDataPoint()
    {
        if (SelectedNetworkDevice is null || NewIoMapping is null)
        {
            return;
        }

        var validationError = ValidateDraft(NewIoMapping);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            SetError(validationError);
            return;
        }

        var standardSignal = NewIoMapping.IsStandardSource ? SelectedStandardIoSignal : null;
        var category = standardSignal?.Category
            ?? IoMappingOptionCatalog.NormalizeCategory(
                NewIoMapping.Category,
                NewIoMapping.AddressCount);
        var businessGroup = standardSignal?.BusinessGroup
            ?? (string.IsNullOrWhiteSpace(NewIoMapping.BusinessGroup)
                ? CreateCustomBusinessGroup(category)
                : NewIoMapping.BusinessGroup.Trim());
        var signalKey = standardSignal?.SignalKey ?? CreateManualSignalKey();
        var signalName = standardSignal?.SignalName ?? NewIoMapping.SignalName.Trim();
        var addressCount = standardSignal?.AddressCount ?? NewIoMapping.AddressCount;
        var dataType = standardSignal?.DataType ?? NewIoMapping.DataType.Trim();
        var direction = standardSignal?.Direction ?? IoMappingOptionCatalog.DirectionRead;
        var sortOrder = standardSignal?.SortOrder > 0 ? standardSignal.SortOrder : NextSortOrder();

        IoMappings.Add(new IoMappingVm
        {
            NetworkDeviceId = SelectedNetworkDevice.Id,
            SignalKey = signalKey,
            PlcAddress = NewIoMapping.PlcAddress.Trim(),
            AddressCount = addressCount,
            DataType = dataType,
            Direction = direction,
            Category = category,
            BusinessGroup = businessGroup,
            SignalName = signalName,
            SortOrder = sortOrder,
            Remark = string.IsNullOrWhiteSpace(NewIoMapping.Remark) ? null : NewIoMapping.Remark.Trim()
        });

        IoMappingsView.Refresh();
        CloseAddIoMappingDialog();
        ClearFeedback();
    }

    private void CloseAddIoMappingDialog()
    {
        IsAddIoMappingDialogOpen = false;
        IsInteractionPairDialog = false;
        SelectedStandardIoSignal = null;
        SelectedStandardInteractionGroup = null;
        NewIoMapping = null;
        NewInteractionPair = null;
        _confirmAddIoMappingCommand.RaiseCanExecuteChanged();
    }

    private string? ValidateDraft(IoMappingDraftVm draft)
    {
        if (!IoMappingOptionCatalog.IsKnownPointSource(draft.Source))
        {
            return GetText("Navigation_Hardware_Validation_IoSourceRequired", "请选择点位来源。");
        }

        if (draft.IsStandardSource)
        {
            if (SelectedStandardIoSignal is null)
            {
                return GetText("Navigation_Hardware_Validation_StandardSignalRequired", "请选择插件标准信号。");
            }

            if (string.Equals(
                    IoMappingOptionCatalog.NormalizeCategory(SelectedStandardIoSignal.Category, SelectedStandardIoSignal.AddressCount),
                    IoMappingOptionCatalog.CategoryInteraction,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GetText("Navigation_Hardware_Validation_DataPointOnly", "新增数据点不能选择信号交互点位。");
            }

            if (IoMappings.Any(x => string.Equals(
                    x.SignalKey,
                    SelectedStandardIoSignal.SignalKey,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return GetText("Navigation_Hardware_Validation_StandardSignalExists", "该插件标准信号已存在，不能重复添加。");
            }

            if (string.IsNullOrWhiteSpace(draft.PlcAddress))
            {
                return GetText("Navigation_Hardware_Validation_IoAddressRequired", "PLC 地址不能为空。");
            }

            return null;
        }

        if (!IoMappingOptionCatalog.IsKnownCategory(draft.Category))
        {
            return GetText("Navigation_Hardware_Validation_IoCategoryRequired", "请选择点位类型。");
        }

        if (string.Equals(draft.Category, IoMappingOptionCatalog.CategoryInteraction, StringComparison.OrdinalIgnoreCase))
        {
            return GetText("Navigation_Hardware_Validation_DataPointOnly", "新增数据点只能选择单点读数据或连续读数据。");
        }

        if (!IoMappingOptionCatalog.IsKnownDataType(draft.DataType))
        {
            return GetText("Navigation_Hardware_Validation_IoDataTypeRequired", "请选择 IO 数据类型。");
        }

        if (string.IsNullOrWhiteSpace(draft.PlcAddress))
        {
            return GetText("Navigation_Hardware_Validation_IoAddressRequired", "PLC 地址不能为空。");
        }

        if (draft.AddressCount <= 0)
        {
            return GetText("Navigation_Hardware_Validation_IoAddressCountPositive", "地址数量必须大于 0。");
        }

        if (string.IsNullOrWhiteSpace(draft.SignalName))
        {
            return GetText("Navigation_Hardware_Validation_IoSignalNameRequired", "信号名称不能为空。");
        }

        return null;
    }

    private void OnNewIoMappingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not IoMappingDraftVm draft)
        {
            return;
        }

        if (e.PropertyName == nameof(IoMappingDraftVm.Source))
        {
            if (draft.IsStandardSource)
            {
                SelectedStandardIoSignal ??= FindStandardDataSignal();
                ApplyStandardSignalToDraft(SelectedStandardIoSignal);
            }
            else if (draft.IsCustomSource)
            {
                var category = IoDataPointCategories.Contains(draft.Category, StringComparer.OrdinalIgnoreCase)
                    ? draft.Category
                    : IoMappingOptionCatalog.CategorySingleRead;
                SelectedStandardIoSignal = null;
                ApplyCustomDebugDefaults(draft, category);
            }
        }
    }

    private void ApplyStandardSignalToDraft(IoStandardSignalOptionVm? signal)
    {
        if (NewIoMapping is null || !NewIoMapping.IsStandardSource || signal is null)
        {
            return;
        }

        NewIoMapping.Category = signal.Category;
        NewIoMapping.Direction = signal.Direction;
        NewIoMapping.PlcAddress = signal.PlcAddress;
        NewIoMapping.AddressCount = signal.AddressCount;
        NewIoMapping.DataType = signal.DataType;
        NewIoMapping.BusinessGroup = signal.BusinessGroup;
        NewIoMapping.SignalName = signal.SignalName;
        NewIoMapping.Remark = signal.Remark;
    }

    private void OnNewInteractionPairPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not IoInteractionPairDraftVm draft)
        {
            return;
        }

        if (e.PropertyName == nameof(IoInteractionPairDraftVm.Source))
        {
            if (draft.IsStandardSource)
            {
                SelectedStandardInteractionGroup ??= StandardInteractionGroups.FirstOrDefault(static x => x.HasReadAndWrite);
                ApplyStandardInteractionGroupToDraft(SelectedStandardInteractionGroup);
            }
            else if (draft.IsCustomSource)
            {
                SelectedStandardInteractionGroup = null;
                ApplyCustomInteractionDefaults(draft);
            }
        }
    }

    private void ApplyStandardInteractionGroupToDraft(IoStandardSignalGroupOptionVm? group)
    {
        if (NewInteractionPair is null || !NewInteractionPair.IsStandardSource || group is null)
        {
            return;
        }

        var read = group.ReadSignals.FirstOrDefault();
        var write = group.WriteSignals.FirstOrDefault();
        NewInteractionPair.BusinessGroup = group.BusinessGroup;
        NewInteractionPair.ReadPlcAddress = read?.PlcAddress ?? string.Empty;
        NewInteractionPair.ReadAddressCount = read?.AddressCount ?? 1;
        NewInteractionPair.ReadDataType = read?.DataType ?? IoMappingOptionCatalog.DataTypeInt16;
        NewInteractionPair.ReadSignalName = read?.SignalName ?? "PLC 触发";
        NewInteractionPair.WritePlcAddress = write?.PlcAddress ?? string.Empty;
        NewInteractionPair.WriteAddressCount = write?.AddressCount ?? 1;
        NewInteractionPair.WriteDataType = write?.DataType ?? IoMappingOptionCatalog.DataTypeInt16;
        NewInteractionPair.WriteSignalName = write?.SignalName ?? "上位机应答";
        NewInteractionPair.Remark = read?.Remark ?? write?.Remark;
    }

    private static void ApplyCustomInteractionDefaults(IoInteractionPairDraftVm draft)
    {
        draft.BusinessGroup = "自定义信号交互";
        draft.ReadAddressCount = 1;
        draft.ReadDataType = IoMappingOptionCatalog.DataTypeInt16;
        draft.ReadSignalName = "PLC 触发";
        draft.WriteAddressCount = 1;
        draft.WriteDataType = IoMappingOptionCatalog.DataTypeInt16;
        draft.WriteSignalName = "上位机应答";
        draft.Remark = null;
    }

    private static void ApplyCustomDebugDefaults(IoMappingDraftVm draft, string category)
    {
        draft.Category = category;
        draft.Direction = IoMappingOptionCatalog.DirectionRead;
        draft.AddressCount = string.Equals(category, IoMappingOptionCatalog.CategoryContinuousRead, StringComparison.OrdinalIgnoreCase)
            ? 10
            : 1;
        draft.DataType = IoMappingOptionCatalog.DataTypeInt16;
        draft.BusinessGroup = CreateCustomBusinessGroup(category);
        draft.SignalName = string.Empty;
        draft.Remark = null;
    }

    private static string CreateCustomBusinessGroup(string category)
    {
        if (string.Equals(category, IoMappingOptionCatalog.CategoryInteraction, StringComparison.OrdinalIgnoreCase))
        {
            return "自定义信号交互";
        }

        if (string.Equals(category, IoMappingOptionCatalog.CategoryContinuousRead, StringComparison.OrdinalIgnoreCase))
        {
            return "自定义连续数据";
        }

        return "自定义单点数据";
    }

    private string? ValidateInteractionPairDraft(IoInteractionPairDraftVm draft)
    {
        if (!IoMappingOptionCatalog.IsKnownPointSource(draft.Source))
        {
            return GetText("Navigation_Hardware_Validation_IoSourceRequired", "请选择点位来源。");
        }

        if (draft.IsStandardSource)
        {
            if (SelectedStandardInteractionGroup is null)
            {
                return GetText("Navigation_Hardware_Validation_InteractionGroupRequired", "请选择插件标准信号交互组。");
            }

            if (!SelectedStandardInteractionGroup.HasReadAndWrite)
            {
                return GetText("Navigation_Hardware_Validation_InteractionGroupIncomplete", "信号交互组必须同时包含 PLC→PC 读点和 PC→PLC 写点。");
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(draft.BusinessGroup))
        {
            return GetText("Navigation_Hardware_Validation_InteractionBusinessGroupRequired", "信号交互业务组不能为空。");
        }

        if (string.IsNullOrWhiteSpace(draft.ReadPlcAddress) || string.IsNullOrWhiteSpace(draft.WritePlcAddress))
        {
            return GetText("Navigation_Hardware_Validation_InteractionAddressRequired", "信号交互必须同时填写读地址和写地址。");
        }

        if (draft.ReadAddressCount <= 0 || draft.WriteAddressCount <= 0)
        {
            return GetText("Navigation_Hardware_Validation_IoAddressCountPositive", "地址数量必须大于 0。");
        }

        if (!IoMappingOptionCatalog.IsKnownDataType(draft.ReadDataType)
            || !IoMappingOptionCatalog.IsKnownDataType(draft.WriteDataType))
        {
            return GetText("Navigation_Hardware_Validation_IoDataTypeRequired", "请选择 IO 数据类型。");
        }

        if (string.IsNullOrWhiteSpace(draft.ReadSignalName) || string.IsNullOrWhiteSpace(draft.WriteSignalName))
        {
            return GetText("Navigation_Hardware_Validation_IoSignalNameRequired", "信号名称不能为空。");
        }

        return null;
    }

    private IReadOnlyCollection<ValidationIssue> ValidateInteractionPairs()
    {
        return IoMappings
            .Where(IsInteractionMapping)
            .GroupBy(
                static x => string.IsNullOrWhiteSpace(x.BusinessGroup) ? x.SignalKey : x.BusinessGroup.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Where(static group =>
            {
                var hasRead = group.Any(x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionRead, StringComparison.OrdinalIgnoreCase));
                var hasWrite = group.Any(x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionWrite, StringComparison.OrdinalIgnoreCase));
                return !hasRead || !hasWrite;
            })
            .Select(group => new ValidationIssue($"信号交互组“{group.Key}”必须同时包含 PLC→PC 读点和 PC→PLC 写点。", nameof(IoMappingVm.BusinessGroup)))
            .ToArray();
    }

    private void DeleteIoMapping(IoMappingVm? selected)
    {
        if (selected is null)
        {
            return;
        }

        if (IsInteractionMapping(selected))
        {
            var businessGroup = selected.BusinessGroup?.Trim() ?? string.Empty;
            var removeItems = IoMappings
                .Where(x => IsInteractionMapping(x)
                    && x.NetworkDeviceId == selected.NetworkDeviceId
                    && string.Equals(x.BusinessGroup?.Trim() ?? string.Empty, businessGroup, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var item in removeItems)
            {
                IoMappings.Remove(item);
            }
        }
        else
        {
            IoMappings.Remove(selected);
        }

        IoMappingsView.Refresh();
        _deleteIoMappingCommand.RaiseCanExecuteChanged();
    }

    private IoMappingVm CreateMappingFromTemplate(ModuleIoTemplateEntry template, string plcAddress)
        => new()
        {
            NetworkDeviceId = SelectedNetworkDevice?.Id ?? 0,
            SignalKey = template.SignalKey,
            PlcAddress = plcAddress.Trim(),
            AddressCount = template.AddressCount,
            DataType = template.DataType,
            Direction = template.Direction,
            Category = template.Category,
            BusinessGroup = template.BusinessGroup,
            SignalName = template.SignalName,
            SortOrder = template.SortOrder,
            Remark = template.Remark
        };

    private IoMappingVm CreateManualMapping(
        string signalKey,
        string plcAddress,
        int addressCount,
        string dataType,
        string direction,
        string category,
        string businessGroup,
        string signalName,
        int sortOrder,
        string? remark)
        => new()
        {
            NetworkDeviceId = SelectedNetworkDevice?.Id ?? 0,
            SignalKey = signalKey,
            PlcAddress = plcAddress.Trim(),
            AddressCount = addressCount,
            DataType = dataType.Trim(),
            Direction = direction,
            Category = category,
            BusinessGroup = businessGroup.Trim(),
            SignalName = signalName.Trim(),
            SortOrder = sortOrder,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim()
        };

    private static bool IsInteractionMapping(IoMappingVm mapping)
        => string.Equals(
            IoMappingOptionCatalog.NormalizeCategory(mapping.Category, mapping.AddressCount),
            IoMappingOptionCatalog.CategoryInteraction,
            StringComparison.OrdinalIgnoreCase);

    private int NextSortOrder()
    {
        var maxManual = IoMappings
            .Where(static x => IsManualSignal(x))
            .Select(static x => x.SortOrder)
            .DefaultIfEmpty(ManualSortOrderBase - 1)
            .Max();

        return Math.Max(maxManual + 1, ManualSortOrderBase);
    }

    private static string CreateManualSignalKey()
        => $"Manual.{Guid.NewGuid():N}";

    private static IoMappingVm CloneIoMapping(IoMappingVm source)
        => new()
        {
            Id = source.Id,
            NetworkDeviceId = source.NetworkDeviceId,
            SignalKey = source.SignalKey,
            PlcAddress = source.PlcAddress,
            AddressCount = source.AddressCount,
            DataType = source.DataType,
            Direction = source.Direction,
            Category = source.Category,
            BusinessGroup = source.BusinessGroup,
            SignalName = source.SignalName,
            SortOrder = source.SortOrder,
            Remark = source.Remark
        };
}
