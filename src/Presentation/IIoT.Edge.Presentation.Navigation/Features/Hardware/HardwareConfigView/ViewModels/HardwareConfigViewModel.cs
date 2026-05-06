using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Data;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public class HardwareConfigViewModel : LocalizedCrudPageViewModelBase
{
    // 自定义点位 SortOrder 起始号段，必须远高于任何模块标准 profile 的最大 SortOrder，
    // 避免 Manual.<guid> 调试点位与标准信号抢占 PLC 连续读所依赖的固定号段。
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
    private readonly BaseCommand _openAddIoMappingDialogCommand;
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
    public IReadOnlyList<string> IoDirections => IoMappingOptionCatalog.Directions;
    public IReadOnlyList<string> IoDataTypes => IoMappingOptionCatalog.DataTypes;
    public IReadOnlyList<string> IoPointSources => IoMappingOptionCatalog.PointSources;

    public ObservableCollection<IoStandardSignalOptionVm> StandardIoSignals { get; } = new();

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

    private string _moduleTemplateHint = "请选择 PLC 设备后补齐默认点位。";
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
            ModuleTemplateHint = "请选择 PLC 设备后补齐默认点位。";
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
    public ICommand OpenAddIoMappingDialogCommand { get; }
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
        _openAddIoMappingDialogCommand = new BaseCommand(
            _ => OpenAddIoMappingDialog(),
            _ => CanEdit && SelectedNetworkDevice is not null);
        _confirmAddIoMappingCommand = new BaseCommand(
            _ => ConfirmAddIoMapping(),
            _ => CanEdit && IsAddIoMappingDialogOpen && NewIoMapping is not null);
        _cancelAddIoMappingDialogCommand = new BaseCommand(_ => CloseAddIoMappingDialog());
        _deleteIoMappingCommand = (BaseCommand)CreateDeleteCommand(IoMappings, () => CanEdit);
        _applyModuleTemplateCommand = (AsyncCommand)CreateBusyCommand(
            ApplyModuleTemplateAsync,
            () => CanApplyModuleTemplate);
        _saveCommand = (AsyncCommand)CreateBusyCommand(SaveAsync, () => CanEdit);

        AddNetworkDeviceCommand = _addNetworkDeviceCommand;
        DeleteNetworkDeviceCommand = _deleteNetworkDeviceCommand;
        AddSerialDeviceCommand = _addSerialDeviceCommand;
        DeleteSerialDeviceCommand = _deleteSerialDeviceCommand;
        OpenAddIoMappingDialogCommand = _openAddIoMappingDialogCommand;
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
        ReplaceItems(
            StandardIoSignals,
            result.DefaultSignals.Select(static x => new IoStandardSignalOptionVm(x)));
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

    // 标准 profile 的 SortOrder 由模块强约束（决定 PLC 连续读合并的号段），保存时必须保留原值；
    // Manual.<guid> 自定义点位独立放在 ≥ ManualSortOrderBase 段，避免与标准号段冲突。
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
        _openAddIoMappingDialogCommand.RaiseCanExecuteChanged();
        _confirmAddIoMappingCommand.RaiseCanExecuteChanged();
        _cancelAddIoMappingDialogCommand.RaiseCanExecuteChanged();
        _deleteIoMappingCommand.RaiseCanExecuteChanged();
        _applyModuleTemplateCommand.RaiseCanExecuteChanged();
        _saveCommand.RaiseCanExecuteChanged();
    }

    private void SetModuleTemplateAvailable(bool value)
    {
        _hasModuleTemplate = value;
        OnPropertyChanged(nameof(CanApplyModuleTemplate));
        _applyModuleTemplateCommand.RaiseCanExecuteChanged();
    }

    private void OpenAddIoMappingDialog()
    {
        if (SelectedNetworkDevice is null)
        {
            SetError(GetText("Navigation_Hardware_Validation_SelectNetworkDeviceFirst", "请先选择一个 PLC 设备。"));
            return;
        }

        var draft = new IoMappingDraftVm
        {
            Source = StandardIoSignals.Count > 0
                ? IoMappingOptionCatalog.PointSourceStandardSignal
                : IoMappingOptionCatalog.PointSourceCustomDebug
        };

        NewIoMapping = draft;
        SelectedStandardIoSignal = draft.IsStandardSource
            ? StandardIoSignals.FirstOrDefault()
            : null;
        if (draft.IsCustomSource)
        {
            ApplyCustomDebugDefaults(draft);
        }

        IsAddIoMappingDialogOpen = true;
        _confirmAddIoMappingCommand.RaiseCanExecuteChanged();
    }

    private void ConfirmAddIoMapping()
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
                ? "自定义点位"
                : NewIoMapping.BusinessGroup.Trim());
        var signalKey = standardSignal?.SignalKey ?? CreateManualSignalKey();
        var signalName = standardSignal?.SignalName ?? NewIoMapping.SignalName.Trim();
        var addressCount = standardSignal?.AddressCount ?? NewIoMapping.AddressCount;
        var dataType = standardSignal?.DataType ?? NewIoMapping.DataType.Trim();
        var direction = standardSignal?.Direction ?? NewIoMapping.Direction.Trim();
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
        SelectedStandardIoSignal = null;
        NewIoMapping = null;
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

        if (!IoMappingOptionCatalog.IsKnownDirection(draft.Direction))
        {
            return GetText("Navigation_Hardware_Validation_IoDirectionRequired", "请选择 IO 方向。");
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
                SelectedStandardIoSignal ??= StandardIoSignals.FirstOrDefault();
                ApplyStandardSignalToDraft(SelectedStandardIoSignal);
            }
            else if (draft.IsCustomSource)
            {
                SelectedStandardIoSignal = null;
                ApplyCustomDebugDefaults(draft);
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

    private static void ApplyCustomDebugDefaults(IoMappingDraftVm draft)
    {
        draft.Category = IoMappingOptionCatalog.CategorySingleRead;
        draft.Direction = IoMappingOptionCatalog.DirectionRead;
        draft.AddressCount = 1;
        draft.DataType = IoMappingOptionCatalog.DataTypeInt16;
        draft.BusinessGroup = "自定义点位";
        draft.SignalName = string.Empty;
        draft.Remark = null;
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
