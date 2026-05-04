using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
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
    private readonly BaseCommand _addReadIoMappingCommand;
    private readonly BaseCommand _addWriteIoMappingCommand;
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
    public ICommand AddReadIoMappingCommand { get; }
    public ICommand AddWriteIoMappingCommand { get; }
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
        _addReadIoMappingCommand = (BaseCommand)CreateScopedAddCommand(
            () => SelectedNetworkDevice is null ? null : IoMappings,
            () => CreateIoMapping("Read"),
            () => CanEdit && SelectedNetworkDevice is not null);
        _addWriteIoMappingCommand = (BaseCommand)CreateScopedAddCommand(
            () => SelectedNetworkDevice is null ? null : IoMappings,
            () => CreateIoMapping("Write"),
            () => CanEdit && SelectedNetworkDevice is not null);
        _deleteIoMappingCommand = (BaseCommand)CreateDeleteCommand(IoMappings, () => CanEdit);
        _applyModuleTemplateCommand = (AsyncCommand)CreateBusyCommand(
            ApplyModuleTemplateAsync,
            () => CanApplyModuleTemplate);
        _saveCommand = (AsyncCommand)CreateBusyCommand(SaveAsync, () => CanEdit);

        AddNetworkDeviceCommand = _addNetworkDeviceCommand;
        DeleteNetworkDeviceCommand = _deleteNetworkDeviceCommand;
        AddSerialDeviceCommand = _addSerialDeviceCommand;
        DeleteSerialDeviceCommand = _deleteSerialDeviceCommand;
        AddReadIoMappingCommand = _addReadIoMappingCommand;
        AddWriteIoMappingCommand = _addWriteIoMappingCommand;
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

    private IReadOnlyCollection<IoMappingVm> BuildMappingsToSave()
        => IoMappings.Select(CloneIoMapping).ToList();

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
        _addReadIoMappingCommand.RaiseCanExecuteChanged();
        _addWriteIoMappingCommand.RaiseCanExecuteChanged();
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

    private IoMappingVm CreateIoMapping(string direction)
    {
        var isWrite = string.Equals(direction, "Write", StringComparison.OrdinalIgnoreCase);
        return new IoMappingVm
        {
            NetworkDeviceId = SelectedNetworkDevice!.Id,
            Direction = isWrite ? "Write" : "Read",
            DataType = "Int16",
            AddressCount = 1,
            Category = GetText(
                isWrite ? "Navigation_Io_Category_SingleWrite" : "Navigation_Io_Category_SingleRead",
                isWrite ? "单点写数据" : "单点读数据")
        };
    }

    private static IoMappingVm CloneIoMapping(IoMappingVm source)
        => new()
        {
            Id = source.Id,
            NetworkDeviceId = source.NetworkDeviceId,
            Label = source.Label,
            PlcAddress = source.PlcAddress,
            AddressCount = source.AddressCount,
            DataType = source.DataType,
            Direction = source.Direction,
            Category = source.Category,
            GroupName = source.GroupName,
            DisplayRole = source.DisplayRole,
            SortOrder = source.SortOrder,
            Remark = source.Remark
        };
}
