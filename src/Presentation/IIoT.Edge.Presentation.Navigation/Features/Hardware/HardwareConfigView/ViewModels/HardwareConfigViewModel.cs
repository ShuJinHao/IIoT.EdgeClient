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
            SelectedIoMapping = null;
            SetModuleTemplateAvailable(false);
            ReplaceItems(StandardIoSignals, Array.Empty<IoStandardSignalOptionVm>());
            ReplaceItems(StandardDataSignals, Array.Empty<IoStandardSignalOptionVm>());
            ReplaceItems(FilteredStandardDataSignals, Array.Empty<IoStandardSignalOptionVm>());
            ReplaceItems(StandardInteractionGroups, Array.Empty<IoStandardSignalGroupOptionVm>());
            ModuleTemplateHint = "请选择 PLC 设备后导入插件标准点位。";
            RefreshAddCommands();
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
            _ => DeleteSelectedIoMapping(),
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
            SelectedIoMapping = null;
            IoMappingsView.Refresh();
            return;
        }

        var result = await _crudService.LoadIoMappingsAsync(SelectedNetworkDevice.Id);
        ReplaceItems(IoMappings, result.Items);
        SelectedIoMapping = null;
        IoMappingsView.Refresh();
    }

    private async Task RefreshModuleTemplateInfoAsync()
    {
        var result = await _crudService.GetModuleTemplateInfoAsync(SelectedNetworkDevice);
        var defaultSignals = result.DefaultSignals.ToArray();
        var candidateSignals = result.CandidateSignals.Count == 0
            ? defaultSignals
            : result.CandidateSignals.ToArray();
        ReplaceItems(StandardIoSignals, defaultSignals.Select(static x => new IoStandardSignalOptionVm(x)));
        ReplaceItems(
            StandardDataSignals,
            candidateSignals
                .Where(static x => IoMappingOptionCatalog.IsDataPointCategory(
                    IoMappingOptionCatalog.NormalizeCategory(x.Category, x.AddressCount)))
                .Select(static x => new IoStandardSignalOptionVm(x)));
        RefreshFilteredStandardDataSignals();
        ReplaceItems(
            StandardInteractionGroups,
            candidateSignals
                .Where(static x => string.Equals(
                    IoMappingOptionCatalog.NormalizeCategory(x.Category, x.AddressCount),
                    IoMappingOptionCatalog.CategoryInteraction,
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(static x => x.SignalKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(static x => new IoStandardSignalGroupOptionVm(
                    x.FirstOrDefault()?.BusinessGroup ?? x.Key,
                    x.ToArray()))
                .OrderBy(static x => x.BusinessGroup, StringComparer.OrdinalIgnoreCase));
        ModuleTemplateHint = result.Message;
        SetModuleTemplateAvailable(result.IsAvailable);
    }

    private async Task<CrudOperationResult> ApplyModuleTemplateAsync()
    {
        if (!ConfirmResetModuleTemplate())
        {
            return CrudOperationResult.Success("已取消重置标准点位。");
        }

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
        if (standardGroup is null)
        {
            SetError(GetText("Navigation_Hardware_Validation_InteractionGroupRequired", "当前 PLC 没有可添加的插件标准信号交互。"));
            return;
        }

        var draft = new IoInteractionPairDraftVm
        {
            Source = IoMappingOptionCatalog.PointSourceStandardSignal
        };

        NewInteractionPair = draft;
        NewIoMapping = null;
        SelectedStandardIoSignal = null;
        SelectedStandardInteractionGroup = standardGroup;

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
        var initialCategory = FindInitialDataPointCategory();

        var draft = new IoMappingDraftVm
        {
            Source = IoMappingOptionCatalog.PointSourceStandardSignal,
            Category = initialCategory
        };

        NewIoMapping = draft;
        NewInteractionPair = null;
        SelectedStandardInteractionGroup = null;
        RefreshFilteredStandardDataSignals();
        SelectedStandardIoSignal = FindStandardDataSignal();
        if (SelectedStandardIoSignal is null)
        {
            ClearStandardSignalDraftForCurrentCategory();
        }

        IsAddIoMappingDialogOpen = true;
        _confirmAddIoMappingCommand.RaiseCanExecuteChanged();
    }

    private IoStandardSignalOptionVm? FindStandardDataSignal()
        => FilteredStandardDataSignals.FirstOrDefault();

    private string FindInitialDataPointCategory()
    {
        var singleRead = StandardDataSignals.FirstOrDefault(static signal => string.Equals(
            IoMappingOptionCatalog.NormalizeCategory(signal.Category, signal.AddressCount),
            IoMappingOptionCatalog.CategorySingleRead,
            StringComparison.OrdinalIgnoreCase));
        var firstSignal = singleRead ?? StandardDataSignals
            .OrderBy(static signal => signal.SortOrder)
            .ThenBy(static signal => signal.DisplayText, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return firstSignal is null
            ? IoMappingOptionCatalog.CategorySingleRead
            : IoMappingOptionCatalog.NormalizeCategory(firstSignal.Category, firstSignal.AddressCount);
    }

    private void RefreshFilteredStandardDataSignals()
    {
        var category = NewIoMapping?.Category ?? IoMappingOptionCatalog.CategorySingleRead;
        var normalizedCategory = IoMappingOptionCatalog.NormalizeCategory(category, addressCount: 1);
        ReplaceItems(
            FilteredStandardDataSignals,
            StandardDataSignals
                .Where(signal => string.Equals(
                    IoMappingOptionCatalog.NormalizeCategory(signal.Category, signal.AddressCount),
                    normalizedCategory,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(static signal => signal.SortOrder)
                .ThenBy(static signal => signal.DisplayText, StringComparer.OrdinalIgnoreCase));
    }

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

        var group = SelectedStandardInteractionGroup!;
        var readTemplate = group.ReadSignals.First();
        var writeTemplate = group.WriteSignals.First();
        var readExists = HasIoMapping(readTemplate.SignalKey, IoMappingOptionCatalog.DirectionRead);
        var writeExists = HasIoMapping(writeTemplate.SignalKey, IoMappingOptionCatalog.DirectionWrite);

        if (readExists || writeExists)
        {
            SetError(GetText("Navigation_Hardware_Validation_InteractionGroupExists", "该信号交互已存在映射，新增必须一次生成读写成对点位；请先删除旧映射后再重新新增。"));
            return;
        }

        IoMappings.Add(CreateMappingFromTemplate(
            readTemplate,
            NewInteractionPair.ReadPlcAddress,
            NewInteractionPair.ReadAddressCount,
            NewInteractionPair.ReadDataType,
            NewInteractionPair.ReadSignalName));
        IoMappings.Add(CreateMappingFromTemplate(
            writeTemplate,
            NewInteractionPair.WritePlcAddress,
            NewInteractionPair.WriteAddressCount,
            NewInteractionPair.WriteDataType,
            NewInteractionPair.WriteSignalName));

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

        var standardSignal = SelectedStandardIoSignal!;
        var category = IoMappingOptionCatalog.NormalizeCategory(NewIoMapping.Category, NewIoMapping.AddressCount);
        var direction = IoMappingOptionCatalog.GetDirectionForCategory(category) ?? standardSignal.Direction;
        var addressCount = IoMappingOptionCatalog.NormalizeAddressCount(category, NewIoMapping.AddressCount);

        IoMappings.Add(new IoMappingVm
        {
            NetworkDeviceId = SelectedNetworkDevice.Id,
            SignalKey = standardSignal.SignalKey,
            PlcAddress = NewIoMapping.PlcAddress.Trim(),
            Category = category,
            AddressCount = addressCount,
            DataType = NewIoMapping.DataType.Trim(),
            Direction = direction,
            BusinessGroup = standardSignal.BusinessGroup,
            SignalName = NewIoMapping.SignalName.Trim(),
            SortOrder = standardSignal.SortOrder,
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
                return GetText("Navigation_Hardware_Validation_NoEnumSignalForCategory", "当前分类暂无插件枚举信号。");
            }

            var draftCategory = IoMappingOptionCatalog.NormalizeCategory(draft.Category, draft.AddressCount);
            var standardCategory = IoMappingOptionCatalog.NormalizeCategory(
                SelectedStandardIoSignal.Category,
                SelectedStandardIoSignal.AddressCount);

            if (!IoMappingOptionCatalog.IsDataPointCategory(draftCategory))
            {
                return GetText("Navigation_Hardware_Validation_DataPointOnly", "新增数据点不能选择信号交互点位。");
            }

            if (!string.Equals(draftCategory, standardCategory, StringComparison.OrdinalIgnoreCase))
            {
                return GetText("Navigation_Hardware_Validation_DataCategoryMismatch", "请选择当前 IO 分类下的插件标准数据点。");
            }

            if (IoMappings.Any(x => string.Equals(
                    x.SignalKey,
                    SelectedStandardIoSignal.SignalKey,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Direction, IoMappingOptionCatalog.GetDirectionForCategory(draftCategory), StringComparison.OrdinalIgnoreCase)))
            {
                return GetText("Navigation_Hardware_Validation_StandardSignalExists", "该插件标准信号已存在，不能重复添加。");
            }

            if (string.IsNullOrWhiteSpace(draft.PlcAddress))
            {
                return GetText("Navigation_Hardware_Validation_IoAddressRequired", "PLC 地址不能为空。");
            }

            if (IoMappingOptionCatalog.IsFixedAddressCountCategory(draftCategory) && draft.AddressCount != 1)
            {
                return GetText("Navigation_Hardware_Validation_FixedCountMustBeOne", "信号交互、单点读数据和单点写数据的数量必须为 1。");
            }

            if (draft.AddressCount <= 0)
            {
                return GetText("Navigation_Hardware_Validation_IoAddressCountPositive", "地址数量必须大于 0。");
            }

            if (!IoMappingOptionCatalog.IsKnownDataType(draft.DataType))
            {
                return GetText("Navigation_Hardware_Validation_IoDataTypeRequired", "请选择 IO 数据类型。");
            }

            if (string.IsNullOrWhiteSpace(draft.SignalName))
            {
                return GetText("Navigation_Hardware_Validation_IoSignalNameRequired", "信号名称不能为空。");
            }

            return null;
        }

        return GetText("Navigation_Hardware_Validation_DataPointOnly", "新增数据点只能选择插件枚举定义的单点读数据、连续读数据、单点写数据或连续写数据。");
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
                RefreshFilteredStandardDataSignals();
                var nextSignal = FindStandardDataSignal();
                if (!ReferenceEquals(SelectedStandardIoSignal, nextSignal))
                {
                    SelectedStandardIoSignal = nextSignal;
                }
                else
                {
                    ApplyStandardSignalToDraft(nextSignal);
                }
            }
            else if (draft.IsCustomSource)
            {
                draft.Source = IoMappingOptionCatalog.PointSourceStandardSignal;
            }
        }

        if (e.PropertyName == nameof(IoMappingDraftVm.Category))
        {
            RefreshFilteredStandardDataSignals();
            var nextSignal = FindStandardDataSignal();
            if (!ReferenceEquals(SelectedStandardIoSignal, nextSignal))
            {
                SelectedStandardIoSignal = nextSignal;
            }
            else
            {
                ApplyStandardSignalToDraft(nextSignal);
            }
        }
    }

    private void ApplyStandardSignalToDraft(IoStandardSignalOptionVm? signal)
    {
        if (NewIoMapping is null || !NewIoMapping.IsStandardSource)
        {
            return;
        }

        if (signal is null)
        {
            ClearStandardSignalDraftForCurrentCategory();
            return;
        }

        var draftCategory = IoMappingOptionCatalog.NormalizeCategory(NewIoMapping.Category, NewIoMapping.AddressCount);
        var signalCategory = IoMappingOptionCatalog.NormalizeCategory(signal.Category, signal.AddressCount);
        if (!string.Equals(draftCategory, signalCategory, StringComparison.OrdinalIgnoreCase))
        {
            ClearStandardSignalDraftForCurrentCategory();
            return;
        }

        NewIoMapping.Category = draftCategory;
        NewIoMapping.Direction = IoMappingOptionCatalog.GetDirectionForCategory(draftCategory) ?? signal.Direction;
        NewIoMapping.PlcAddress = signal.PlcAddress;
        NewIoMapping.AddressCount = signal.AddressCount;
        NewIoMapping.DataType = signal.DataType;
        NewIoMapping.BusinessGroup = signal.BusinessGroup;
        NewIoMapping.SignalName = signal.SignalName;
        NewIoMapping.Remark = signal.Remark;
    }

    private void ClearStandardSignalDraftForCurrentCategory()
    {
        if (NewIoMapping is null || !NewIoMapping.IsStandardSource)
        {
            return;
        }

        var category = IoMappingOptionCatalog.NormalizeCategory(NewIoMapping.Category, NewIoMapping.AddressCount);
        NewIoMapping.Category = category;
        NewIoMapping.Direction = IoMappingOptionCatalog.GetDirectionForCategory(category)
                                 ?? IoMappingOptionCatalog.DirectionRead;
        NewIoMapping.PlcAddress = string.Empty;
        NewIoMapping.AddressCount = IoMappingOptionCatalog.NormalizeAddressCount(category, NewIoMapping.AddressCount);
        NewIoMapping.DataType = IoMappingOptionCatalog.DataTypeInt16;
        NewIoMapping.BusinessGroup = string.Empty;
        NewIoMapping.SignalName = string.Empty;
        NewIoMapping.Remark = null;
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
                draft.Source = IoMappingOptionCatalog.PointSourceStandardSignal;
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
        draft.Direction = IoMappingOptionCatalog.GetDirectionForCategory(category) ?? IoMappingOptionCatalog.DirectionRead;
        draft.AddressCount = string.Equals(category, IoMappingOptionCatalog.CategoryContinuousRead, StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, IoMappingOptionCatalog.CategoryContinuousWrite, StringComparison.OrdinalIgnoreCase)
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
            return "自定义连续读数据";
        }

        if (string.Equals(category, IoMappingOptionCatalog.CategorySingleWrite, StringComparison.OrdinalIgnoreCase))
        {
            return "自定义单点写数据";
        }

        if (string.Equals(category, IoMappingOptionCatalog.CategoryContinuousWrite, StringComparison.OrdinalIgnoreCase))
        {
            return "自定义连续写数据";
        }

        return "自定义单点读数据";
    }

    private string? ValidateInteractionPairDraft(IoInteractionPairDraftVm draft)
    {
        if (!IoMappingOptionCatalog.IsKnownPointSource(draft.Source))
        {
            return GetText("Navigation_Hardware_Validation_IoSourceRequired", "请选择点位来源。");
        }

        if (!draft.IsStandardSource)
        {
            return GetText("Navigation_Hardware_Validation_InteractionGroupRequired", "信号交互只能选择插件定义的标准业务动作。");
        }

        if (SelectedStandardInteractionGroup is null)
        {
            return GetText("Navigation_Hardware_Validation_InteractionGroupRequired", "请选择插件标准信号交互组。");
        }

        if (!SelectedStandardInteractionGroup.HasReadAndWrite)
        {
            return GetText("Navigation_Hardware_Validation_InteractionGroupIncomplete", "信号交互组必须同时包含 PLC→PC 读点和 PC→PLC 写点。");
        }

        if (string.IsNullOrWhiteSpace(draft.ReadPlcAddress) || string.IsNullOrWhiteSpace(draft.WritePlcAddress))
        {
            return GetText("Navigation_Hardware_Validation_InteractionAddressRequired", "信号交互必须同时填写读地址和写地址。");
        }

        if (draft.ReadAddressCount <= 0 || draft.WriteAddressCount <= 0)
        {
            return GetText("Navigation_Hardware_Validation_IoAddressCountPositive", "地址数量必须大于 0。");
        }

        if (draft.ReadAddressCount != 1 || draft.WriteAddressCount != 1)
        {
            return GetText("Navigation_Hardware_Validation_InteractionCountMustBeOne", "信号交互读点和写点数量必须固定为 1。");
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
            .GroupBy(CreateInteractionGroupKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var issues = new List<ValidationIssue>();
                var displayName = CreateInteractionDisplayName(group.First());
                var readCount = group.Count(x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionRead, StringComparison.OrdinalIgnoreCase));
                var writeCount = group.Count(x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionWrite, StringComparison.OrdinalIgnoreCase));
                if (readCount == 0 || writeCount == 0)
                {
                    issues.Add(new ValidationIssue($"信号交互“{displayName}”必须同时包含 PLC→PC 读点和 PC→PLC 写点。", nameof(IoMappingVm.BusinessGroup)));
                }

                if (readCount > 1)
                {
                    issues.Add(new ValidationIssue($"信号交互“{displayName}”存在重复 PLC→PC 读点。", nameof(IoMappingVm.BusinessGroup)));
                }

                if (writeCount > 1)
                {
                    issues.Add(new ValidationIssue($"信号交互“{displayName}”存在重复 PC→PLC 写点。", nameof(IoMappingVm.BusinessGroup)));
                }

                return issues;
            })
            .ToArray();
    }

    private void DeleteSelectedIoMapping()
    {
        var selected = SelectedIoMapping;
        if (selected is null)
        {
            return;
        }

        if (IsInteractionMapping(selected))
        {
            var interactionKey = CreateInteractionGroupKey(selected);
            var removeItems = IoMappings
                .Where(x => IsInteractionMapping(x)
                    && x.NetworkDeviceId == selected.NetworkDeviceId
                    && string.Equals(CreateInteractionGroupKey(x), interactionKey, StringComparison.OrdinalIgnoreCase))
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

        SelectedIoMapping = null;
        IoMappingsView.Refresh();
        _deleteIoMappingCommand.RaiseCanExecuteChanged();
    }

    private IoMappingVm CreateMappingFromTemplate(
        ModuleIoTemplateEntry template,
        string plcAddress,
        int? addressCount = null,
        string? dataType = null,
        string? signalName = null)
    {
        var category = IoMappingOptionCatalog.NormalizeCategory(template.Category, addressCount ?? template.AddressCount);
        var normalizedCount = IoMappingOptionCatalog.NormalizeAddressCount(category, addressCount ?? template.AddressCount);
        var direction = IoMappingOptionCatalog.GetDirectionForCategory(category) ?? template.Direction;

        return new IoMappingVm
        {
            NetworkDeviceId = SelectedNetworkDevice?.Id ?? 0,
            SignalKey = template.SignalKey,
            PlcAddress = plcAddress.Trim(),
            Category = category,
            AddressCount = normalizedCount,
            DataType = string.IsNullOrWhiteSpace(dataType) ? template.DataType : dataType.Trim(),
            Direction = direction,
            BusinessGroup = template.BusinessGroup,
            SignalName = string.IsNullOrWhiteSpace(signalName) ? template.SignalName : signalName.Trim(),
            SortOrder = template.SortOrder,
            Remark = template.Remark
        };
    }

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
            Category = category,
            AddressCount = addressCount,
            DataType = dataType.Trim(),
            Direction = direction,
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

    private bool HasIoMapping(string signalKey, string direction)
        => IoMappings.Any(x => string.Equals(x.SignalKey, signalKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Direction, direction, StringComparison.OrdinalIgnoreCase));

    private static string CreateInteractionGroupKey(IoMappingVm mapping)
    {
        if (IsStandardInteractionSignalKey(mapping.SignalKey))
        {
            return $"SIGNAL:{mapping.SignalKey.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(mapping.BusinessGroup))
        {
            return $"GROUP:{mapping.BusinessGroup.Trim()}";
        }

        return $"SIGNAL:{mapping.SignalKey?.Trim() ?? string.Empty}";
    }

    private static bool IsStandardInteractionSignalKey(string? signalKey)
        => signalKey?.StartsWith("Homogenization.Interaction.", StringComparison.OrdinalIgnoreCase) ?? false;

    private static string CreateInteractionDisplayName(IoMappingVm mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.BusinessGroup))
        {
            return mapping.BusinessGroup.Trim();
        }

        return string.IsNullOrWhiteSpace(mapping.SignalKey) ? "未命名" : mapping.SignalKey.Trim();
    }

    private static bool ConfirmResetModuleTemplate()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return true;
        }

        var result = System.Windows.MessageBox.Show(
            "重置标准点位会清空当前 PLC 已有 IO 映射，并按插件标准模板重新生成。是否继续？",
            "重置标准点位",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning);

        return result == System.Windows.MessageBoxResult.OK;
    }

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
            Category = source.Category,
            AddressCount = source.AddressCount,
            DataType = source.DataType,
            Direction = source.Direction,
            BusinessGroup = source.BusinessGroup,
            SignalName = source.SignalName,
            SortOrder = source.SortOrder,
            Remark = source.Remark
        };
}
