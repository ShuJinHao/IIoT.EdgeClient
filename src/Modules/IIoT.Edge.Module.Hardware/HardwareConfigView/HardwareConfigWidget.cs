using AutoMapper;
using IIoT.Edge.Common.Enums;
using IIoT.Edge.Common.Mvvm;
using IIoT.Edge.Common.Repository;
using IIoT.Edge.Contracts.Auth;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Hardware.HardwareConfigView.Models;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IIoT.Edge.Module.Hardware.HardwareConfigView;

public class HardwareConfigWidget : WidgetBase
{
    public override string WidgetId => "Hardware.ConfigView";
    public override string WidgetName => "硬件配置";

    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<SerialDeviceEntity> _serialDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly IAuthService _authService;
    private readonly IMapper _mapper;

    private const int IoPageSize = 20;
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
            _selectedNetworkDevice = value;
            OnPropertyChanged();
            IoPageIndex = 0;
            _ = LoadIoMappingsAsync();
        }
    }

    private int _ioPageIndex;
    public int IoPageIndex
    {
        get => _ioPageIndex;
        set { _ioPageIndex = value; OnPropertyChanged(); }
    }

    private int _ioTotalCount;
    public int IoTotalCount
    {
        get => _ioTotalCount;
        set { _ioTotalCount = value; OnPropertyChanged(); }
    }

    public ICommand AddNetworkDeviceCommand { get; }
    public ICommand DeleteNetworkDeviceCommand { get; }
    public ICommand AddSerialDeviceCommand { get; }
    public ICommand DeleteSerialDeviceCommand { get; }
    public ICommand AddIoMappingCommand { get; }
    public ICommand DeleteIoMappingCommand { get; }
    public ICommand IoNextPageCommand { get; }
    public ICommand IoPrevPageCommand { get; }
    public ICommand SaveCommand { get; }

    public HardwareConfigWidget(
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<SerialDeviceEntity> serialDevices,
        IRepository<IoMappingEntity> ioMappings,
        IAuthService authService,
        IMapper mapper)
    {
        _networkDevices = networkDevices;
        _serialDevices = serialDevices;
        _ioMappings = ioMappings;
        _authService = authService;
        _mapper = mapper;

        AddNetworkDeviceCommand = new BaseCommand(_ => AddNetworkDevice(), _ => CanEdit);
        DeleteNetworkDeviceCommand = new BaseCommand(DeleteNetworkDevice, _ => CanEdit);
        AddSerialDeviceCommand = new BaseCommand(_ => AddSerialDevice(), _ => CanEdit);
        DeleteSerialDeviceCommand = new BaseCommand(DeleteSerialDevice, _ => CanEdit);
        AddIoMappingCommand = new BaseCommand(_ => AddIoMapping(), _ => CanEdit);
        DeleteIoMappingCommand = new BaseCommand(DeleteIoMapping, _ => CanEdit);
        IoNextPageCommand = new AsyncCommand(() => IoNextPageAsync());
        IoPrevPageCommand = new BaseCommand(_ => IoPrevPage(), _ => IoPageIndex > 0);
        SaveCommand = new AsyncCommand(SaveAsync);

        _ = LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        var networks = await _networkDevices.GetListAsync(
            _ => true, CancellationToken.None);
        NetworkDevices.Clear();
        foreach (var n in networks)
            NetworkDevices.Add(_mapper.Map<NetworkDeviceVm>(n));

        var serials = await _serialDevices.GetListAsync(
            _ => true, CancellationToken.None);
        SerialDevices.Clear();
        foreach (var s in serials)
            SerialDevices.Add(_mapper.Map<SerialDeviceVm>(s));

        if (NetworkDevices.Count > 0)
            SelectedNetworkDevice = NetworkDevices[0];
    }

    private async Task LoadIoMappingsAsync()
    {
        if (SelectedNetworkDevice is null) return;

        IoTotalCount = await _ioMappings.GetCountAsync(
            x => x.NetworkDeviceId == SelectedNetworkDevice.Id,
            CancellationToken.None);

        var items = await _ioMappings.GetListAsync(
            x => x.NetworkDeviceId == SelectedNetworkDevice.Id,
            CancellationToken.None);

        IoMappings.Clear();
        foreach (var item in items
            .OrderBy(x => x.SortOrder)
            .Skip(IoPageIndex * IoPageSize)
            .Take(IoPageSize))
        {
            IoMappings.Add(_mapper.Map<IoMappingVm>(item));
        }
    }

    private async Task IoNextPageAsync()
    {
        if ((IoPageIndex + 1) * IoPageSize >= IoTotalCount) return;
        IoPageIndex++;
        await LoadIoMappingsAsync();
    }

    private void IoPrevPage()
    {
        if (IoPageIndex <= 0) return;
        IoPageIndex--;
        _ = LoadIoMappingsAsync();
    }

    private void AddNetworkDevice()
    => NetworkDevices.Add(new NetworkDeviceVm { DeviceType = DeviceType.PLC });

    private void DeleteNetworkDevice(object? param)
    {
        if (param is NetworkDeviceVm vm)
            NetworkDevices.Remove(vm);
    }

    private void AddSerialDevice()
        => SerialDevices.Add(new SerialDeviceVm());

    private void DeleteSerialDevice(object? param)
    {
        if (param is SerialDeviceVm vm)
            SerialDevices.Remove(vm);
    }

    private void AddIoMapping()
    {
        if (SelectedNetworkDevice is null) return;
        IoMappings.Add(new IoMappingVm
        {
            NetworkDeviceId = SelectedNetworkDevice.Id,
            Direction = "Read",
            DataType = "Int16",
            AddressCount = 1
        });
    }

    private void DeleteIoMapping(object? param)
    {
        if (param is IoMappingVm vm)
            IoMappings.Remove(vm);
    }

    private async Task SaveAsync()
    {
        foreach (var vm in NetworkDevices)
        {
            var entity = _mapper.Map<NetworkDeviceEntity>(vm);
            if (vm.Id == 0)
                _networkDevices.Add(entity);
            else
                _networkDevices.Update(entity);
        }
        await _networkDevices.SaveChangesAsync();

        foreach (var vm in SerialDevices)
        {
            var entity = _mapper.Map<SerialDeviceEntity>(vm);
            if (vm.Id == 0)
                _serialDevices.Add(entity);
            else
                _serialDevices.Update(entity);
        }
        await _serialDevices.SaveChangesAsync();

        if (SelectedNetworkDevice is not null)
        {
            foreach (var vm in IoMappings)
            {
                var entity = _mapper.Map<IoMappingEntity>(vm);
                if (vm.Id == 0)
                    _ioMappings.Add(entity);
                else
                    _ioMappings.Update(entity);
            }
            await _ioMappings.SaveChangesAsync();
        }
    }
}