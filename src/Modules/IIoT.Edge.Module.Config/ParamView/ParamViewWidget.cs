using AutoMapper;
using IIoT.Edge.Common.Mvvm;
using IIoT.Edge.Common.Repository;
using IIoT.Edge.Contracts.Config;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Config.ParamView.Models;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IIoT.Edge.Module.Config.ParamView;

public class ParamViewWidget : WidgetBase
{
    public override string WidgetId => "Config.ParamView";
    public override string WidgetName => "参数配置";

    private readonly ISystemConfigService _sysConfigService;
    private readonly IDeviceParamService _deviceParamService;
    private readonly IReadRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IMapper _mapper;

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public ObservableCollection<GeneralParamVm> GeneralParams { get; } = new();
    public ObservableCollection<DeviceParamGroupVm> DeviceParamGroups { get; } = new();

    private DeviceParamGroupVm? _selectedGroup;
    public DeviceParamGroupVm? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            _selectedGroup = value;
            OnPropertyChanged();
            if (value != null) _ = LoadDeviceParamsAsync(value);
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand AddGeneralParamCommand { get; }
    public ICommand DeleteGeneralParamCommand { get; }
    public ICommand AddDeviceParamCommand { get; }
    public ICommand DeleteDeviceParamCommand { get; }

    public ParamViewWidget(
        ISystemConfigService sysConfigService,
        IDeviceParamService deviceParamService,
        IReadRepository<NetworkDeviceEntity> networkDevices,
        IMapper mapper)
    {
        _sysConfigService = sysConfigService;
        _deviceParamService = deviceParamService;
        _networkDevices = networkDevices;
        _mapper = mapper;

        SaveCommand = new AsyncCommand(SaveAsync);
        AddGeneralParamCommand = new BaseCommand(
            _ => GeneralParams.Add(new GeneralParamVm()));
        DeleteGeneralParamCommand = new BaseCommand(OnDeleteGeneralParam);
        AddDeviceParamCommand = new BaseCommand(_ =>
        {
            if (SelectedGroup != null)
                SelectedGroup.Params.Add(new DeviceParamVm());
        });
        DeleteDeviceParamCommand = new BaseCommand(OnDeleteDeviceParam);

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await LoadGeneralParamsAsync();
        await LoadDeviceGroupsAsync();
    }

    private async Task LoadGeneralParamsAsync()
    {
        var list = await _sysConfigService.GetAllAsync();
        GeneralParams.Clear();
        foreach (var vm in list.OrderBy(x => x.SortOrder)
            .Select(e => _mapper.Map<GeneralParamVm>(e)))
            GeneralParams.Add(vm);
    }

    private async Task LoadDeviceGroupsAsync()
    {
        var devices = await _networkDevices.GetListAsync(
            x => x.IsEnabled, CancellationToken.None);
        DeviceParamGroups.Clear();
        foreach (var d in devices)
            DeviceParamGroups.Add(new DeviceParamGroupVm
            {
                DeviceId = d.Id,
                DeviceName = $"{d.DeviceName} ({d.IpAddress})"
            });
        if (DeviceParamGroups.Count > 0)
            SelectedGroup = DeviceParamGroups[0];
    }

    private async Task LoadDeviceParamsAsync(DeviceParamGroupVm group)
    {
        var list = await _deviceParamService
            .GetByDeviceAsync(group.DeviceId);
        group.Params.Clear();
        foreach (var vm in list.OrderBy(x => x.SortOrder)
            .Select(e => _mapper.Map<DeviceParamVm>(e)))
            group.Params.Add(vm);
    }

    private void OnDeleteGeneralParam(object? param)
    {
        if (param is GeneralParamVm vm)
            GeneralParams.Remove(vm);
    }

    private void OnDeleteDeviceParam(object? param)
    {
        if (param is DeviceParamVm vm && SelectedGroup != null)
            SelectedGroup.Params.Remove(vm);
    }

    private async Task SaveAsync()
    {
        // 通用参数：手动组装 Entity（调用公开的有参构造函数）
        var sysEntities = GeneralParams
            .Select((vm, idx) =>
            {
                var e = new SystemConfigEntity(
                    key: vm.Name,
                    value: vm.Value,
                    description: vm.Description);

                e.SortOrder = idx + 1;
                return e;
            }).ToList();

        await _sysConfigService.SaveAsync(sysEntities);

        // 设备参数：手动组装 Entity（通过上下文 SelectedGroup 拿到 DeviceId 并传入构造函数）
        if (SelectedGroup != null)
        {
            var deviceEntities = SelectedGroup.Params
                .Select((vm, idx) =>
                {
                    var e = new DeviceParamEntity(
                        networkDeviceId: SelectedGroup.DeviceId,
                        name: vm.Name,
                        value: vm.Value,
                        unit: vm.Unit);

                    e.MinValue = vm.Min;
                    e.MaxValue = vm.Max;
                    e.SortOrder = idx + 1;
                    return e;
                }).ToList();

            await _deviceParamService.SaveAsync(
                SelectedGroup.DeviceId, deviceEntities);
        }

        // 重新加载
        await LoadGeneralParamsAsync();
        if (SelectedGroup != null)
            await LoadDeviceParamsAsync(SelectedGroup);
    }
}