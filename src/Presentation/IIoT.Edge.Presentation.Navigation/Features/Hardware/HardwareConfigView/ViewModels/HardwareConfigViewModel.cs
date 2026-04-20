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
    private readonly IAuthService _authService;
    private readonly IEditorValidator<NetworkDeviceVm> _networkDeviceValidator = new NetworkDeviceValidator();
    private readonly IEditorValidator<SerialDeviceVm> _serialDeviceValidator = new SerialDeviceValidator();
    private readonly IEditorValidator<IoMappingVm> _ioMappingValidator = new IoMappingValidator();
    private readonly AsyncCommand _applyModuleTemplateCommand;
    private readonly BaseCommand _ioPrevPageCommand;
    private readonly string _viewId;
    private readonly string _viewTitle;

    private const int IoPageSize = 20;

    public override string ViewId => _viewId;
    public override string ViewTitle => _viewTitle;

    public IEnumerable<DeviceType> DeviceTypes => Enum.GetValues<DeviceType>();
    public IEnumerable<PlcType> PlcTypes => Enum.GetValues<PlcType>();

    public bool CanEdit => _authService.HasPermission(Permissions.HardwareConfig);

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

    public HardwareConfigViewModel(IHardwareConfigCrudService crudService, IAuthService authService)
        : this(crudService, authService, "Hardware.HardwareConfigView", "硬件配置")
    {
    }

    protected HardwareConfigViewModel(
        IHardwareConfigCrudService crudService,
        IAuthService authService,
        string viewId,
        string viewTitle)
    {
        _crudService = crudService;
        _authService = authService;
        _viewId = viewId;
        _viewTitle = viewTitle;

        AddNetworkDeviceCommand = CreateAddCommand(
            NetworkDevices,
            () => new NetworkDeviceVm { DeviceType = DeviceType.PLC, ModuleId = string.Empty },
            () => CanEdit);
        DeleteNetworkDeviceCommand = CreateDeleteCommand(NetworkDevices, () => CanEdit);
        AddSerialDeviceCommand = CreateAddCommand(
            SerialDevices,
            () => new SerialDeviceVm(),
            () => CanEdit);
        DeleteSerialDeviceCommand = CreateDeleteCommand(SerialDevices, () => CanEdit);
        AddIoMappingCommand = CreateScopedAddCommand(
            () => SelectedNetworkDevice is null ? null : IoMappings,
            () => new IoMappingVm
            {
                NetworkDeviceId = SelectedNetworkDevice!.Id,
                Direction = "Read",
                DataType = "Int16",
                AddressCount = 1
            },
            () => CanEdit && SelectedNetworkDevice is not null);
        DeleteIoMappingCommand = CreateDeleteCommand(IoMappings, () => CanEdit);
        _applyModuleTemplateCommand = (AsyncCommand)CreateBusyCommand(
            ApplyModuleTemplateAsync,
            () => CanApplyModuleTemplate);
        IoNextPageCommand = new AsyncCommand(IoNextPageAsync);
        _ioPrevPageCommand = new BaseCommand(_ => IoPrevPage(), _ => IoPageIndex > 0);
        SaveCommand = CreateBusyCommand(SaveAsync, () => CanEdit);
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
            ReplaceItems(IoMappings, []);
            return;
        }

        var result = await _crudService.LoadIoMappingsAsync(
            SelectedNetworkDevice.Id,
            IoPageIndex,
            IoPageSize);

        IoTotalCount = result.TotalCount;
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

        await _crudService.SaveAsync(
            NetworkDevices,
            SerialDevices,
            SelectedNetworkDevice?.Id ?? 0,
            IoMappings);

        await LoadAllAsync();

        return CrudOperationResult.Success("硬件配置已保存。");
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
}
