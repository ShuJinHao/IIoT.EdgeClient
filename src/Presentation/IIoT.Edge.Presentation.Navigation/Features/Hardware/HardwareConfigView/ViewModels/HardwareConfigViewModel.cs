using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Presentation.Navigation.Common.Crud;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.UI.Shared.Mvvm;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public class HardwareConfigViewModel : CrudPageViewModelBase
{
    private readonly IHardwareConfigCrudService _crudService;
    private readonly IClientPermissionService _permissionService;
    private readonly IEditorValidator<NetworkDeviceVm> _networkDeviceValidator = new NetworkDeviceValidator();
    private readonly IEditorValidator<SerialDeviceVm> _serialDeviceValidator = new SerialDeviceValidator();
    private readonly IEditorValidator<IoMappingVm> _ioMappingValidator = new IoMappingValidator();
    private readonly AsyncCommand _applyModuleTemplateCommand;
    private readonly BaseCommand _addNetworkDeviceCommand;
    private readonly BaseCommand _deleteNetworkDeviceCommand;
    private readonly BaseCommand _addSerialDeviceCommand;
    private readonly BaseCommand _deleteSerialDeviceCommand;
    private readonly BaseCommand _addIoMappingCommand;
    private readonly BaseCommand _deleteIoMappingCommand;
    private readonly AsyncCommand _saveCommand;
    private readonly BaseCommand _ioPrevPageCommand;
    private readonly string _viewId;
    private readonly string _viewTitle;
    private List<IoMappingVm> _loadedIoMappingSnapshot = [];

    private const int IoPageSize = 20;

    public override string ViewId => _viewId;
    public override string ViewTitle => _viewTitle;

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
            ModuleTemplateSummary = string.Empty;
            IoPageIndex = 0;
            _ = RefreshSelectedNetworkDeviceAsync();
        }
    }

    private int _ioPageIndex;
    public int IoPageIndex
    {
        get => _ioPageIndex;
        set
        {
            _ioPageIndex = value;
            OnPropertyChanged();
            _ioPrevPageCommand.RaiseCanExecuteChanged();
        }
    }

    private int _ioTotalCount;
    public int IoTotalCount
    {
        get => _ioTotalCount;
        set { _ioTotalCount = value; OnPropertyChanged(); }
    }

    private string _moduleTemplateSummary = string.Empty;
    public string ModuleTemplateSummary
    {
        get => _moduleTemplateSummary;
        private set
        {
            _moduleTemplateSummary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasModuleTemplateSummary));
            OnPropertyChanged(nameof(CanApplyModuleTemplate));
            _applyModuleTemplateCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasModuleTemplateSummary => !string.IsNullOrWhiteSpace(ModuleTemplateSummary);

    public bool CanApplyModuleTemplate =>
        CanEdit
        && SelectedNetworkDevice is not null
        && SelectedNetworkDevice.DeviceType == DeviceType.PLC
        && SelectedNetworkDevice.Id > 0
        && HasModuleTemplateSummary;

    public ICommand AddNetworkDeviceCommand { get; }
    public ICommand DeleteNetworkDeviceCommand { get; }
    public ICommand AddSerialDeviceCommand { get; }
    public ICommand DeleteSerialDeviceCommand { get; }
    public ICommand AddIoMappingCommand { get; }
    public ICommand DeleteIoMappingCommand { get; }
    public ICommand ApplyModuleTemplateCommand => _applyModuleTemplateCommand;
    public ICommand IoNextPageCommand { get; }
    public ICommand IoPrevPageCommand => _ioPrevPageCommand;
    public ICommand SaveCommand { get; }

    public HardwareConfigViewModel(
        IHardwareConfigCrudService crudService,
        IClientPermissionService permissionService)
        : this(crudService, permissionService, "Hardware.HardwareConfigView", "硬件配置")
    {
    }

    protected HardwareConfigViewModel(
        IHardwareConfigCrudService crudService,
        IClientPermissionService permissionService,
        string viewId,
        string viewTitle)
    {
        _crudService = crudService;
        _permissionService = permissionService;
        _viewId = viewId;
        _viewTitle = viewTitle;

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
        _addIoMappingCommand = (BaseCommand)CreateScopedAddCommand(
            () => SelectedNetworkDevice is null ? null : IoMappings,
            () => new IoMappingVm
            {
                NetworkDeviceId = SelectedNetworkDevice!.Id,
                Direction = "Read",
                DataType = "Int16",
                AddressCount = 1
            },
            () => CanEdit && SelectedNetworkDevice is not null);
        _deleteIoMappingCommand = (BaseCommand)CreateDeleteCommand(IoMappings, () => CanEdit);
        _applyModuleTemplateCommand = (AsyncCommand)CreateBusyCommand(
            ApplyModuleTemplateAsync,
            () => CanApplyModuleTemplate);
        IoNextPageCommand = new AsyncCommand(IoNextPageAsync);
        _ioPrevPageCommand = new BaseCommand(_ => IoPrevPage(), _ => IoPageIndex > 0);
        _saveCommand = (AsyncCommand)CreateBusyCommand(SaveAsync, () => CanEdit);

        AddNetworkDeviceCommand = _addNetworkDeviceCommand;
        DeleteNetworkDeviceCommand = _deleteNetworkDeviceCommand;
        AddSerialDeviceCommand = _addSerialDeviceCommand;
        DeleteSerialDeviceCommand = _deleteSerialDeviceCommand;
        AddIoMappingCommand = _addIoMappingCommand;
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
            ModuleTemplateSummary = string.Empty;
            IoTotalCount = 0;
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
            IoTotalCount = 0;
            _loadedIoMappingSnapshot = [];
            ReplaceItems(IoMappings, []);
            return;
        }

        var result = await _crudService.LoadIoMappingsAsync(
            SelectedNetworkDevice.Id,
            IoPageIndex,
            IoPageSize);

        IoTotalCount = result.TotalCount;
        _loadedIoMappingSnapshot = result.Items.Select(CloneIoMapping).ToList();
        ReplaceItems(IoMappings, result.Items);
    }

    private async Task RefreshModuleTemplateInfoAsync()
    {
        var result = await _crudService.GetModuleTemplateInfoAsync(SelectedNetworkDevice);
        ModuleTemplateSummary = result.IsAvailable ? result.Summary : string.Empty;
    }

    private async Task IoNextPageAsync()
    {
        if ((IoPageIndex + 1) * IoPageSize >= IoTotalCount)
        {
            return;
        }

        IoPageIndex++;
        await LoadIoMappingsAsync();
    }

    private void IoPrevPage()
    {
        if (IoPageIndex <= 0)
        {
            return;
        }

        IoPageIndex--;
        _ = LoadIoMappingsAsync();
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

        var mappingsToSave = await BuildMappingsToSaveAsync();

        var saveResult = await _crudService.SaveAsync(
            NetworkDevices,
            SerialDevices,
            SelectedNetworkDevice?.Id ?? 0,
            mappingsToSave);

        if (saveResult.IsSuccess
            || saveResult.Message.StartsWith("配置已保存，但", StringComparison.Ordinal))
        {
            await LoadAllAsync();
        }

        return saveResult;
    }

    private async Task<IReadOnlyCollection<IoMappingVm>> BuildMappingsToSaveAsync()
    {
        if (SelectedNetworkDevice is null || SelectedNetworkDevice.Id <= 0)
        {
            return IoMappings.ToList();
        }

        var allPersisted = await _crudService.LoadIoMappingsAsync(
            SelectedNetworkDevice.Id,
            0,
            int.MaxValue);

        if (allPersisted.TotalCount == 0)
        {
            return IoMappings.ToList();
        }

        var loadedIds = _loadedIoMappingSnapshot
            .Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();

        var merged = allPersisted.Items
            .Where(x => !loadedIds.Contains(x.Id))
            .Select(CloneIoMapping)
            .ToList();

        merged.AddRange(IoMappings.Select(CloneIoMapping));
        return merged;
    }

    private void OnSelectedNetworkDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkDeviceVm.ModuleId)
            or nameof(NetworkDeviceVm.DeviceType)
            or nameof(NetworkDeviceVm.Id))
        {
            OnPropertyChanged(nameof(CanApplyModuleTemplate));
            _applyModuleTemplateCommand.RaiseCanExecuteChanged();
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
        _addIoMappingCommand.RaiseCanExecuteChanged();
        _deleteIoMappingCommand.RaiseCanExecuteChanged();
        _applyModuleTemplateCommand.RaiseCanExecuteChanged();
        _saveCommand.RaiseCanExecuteChanged();
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
            SortOrder = source.SortOrder,
            Remark = source.Remark
        };
}
