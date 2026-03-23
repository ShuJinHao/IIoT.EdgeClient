using IIoT.Edge.Common.Enums;
using IIoT.Edge.Common.Repository;
using IIoT.Edge.Contracts;
using IIoT.Edge.Contracts.Plc;
using IIoT.Edge.Contracts.Plc.Factory;
using IIoT.Edge.Contracts.Plc.Store;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Module.Hardware.Plc;

public class PlcConnectionManager : IDisposable
{
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly IPlcDataStore _dataStore;
    private readonly IPlcServiceFactory _plcServiceFactory;
    private readonly ILogService _logger;

    private readonly Dictionary<int, IPlcService> _plcInstances = new();
    private readonly Dictionary<int, List<IPlcTask>> _plcTasks = new();
    private readonly Dictionary<int, Func<IPlcService, IoMappingEntity[], List<IPlcTask>>> _taskFactories = new();

    private CancellationTokenSource _cts = new();

    public PlcConnectionManager(
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<IoMappingEntity> ioMappings,
        IPlcDataStore dataStore,
        IPlcServiceFactory plcServiceFactory,
        ILogService logger)
    {
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _dataStore = dataStore;
        _plcServiceFactory = plcServiceFactory;
        _logger = logger;
    }

    public void RegisterTasks(
        int networkDeviceId,
        Func<IPlcService, IoMappingEntity[], List<IPlcTask>> factory)
    {
        _taskFactories[networkDeviceId] = factory;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var devices = await _networkDevices.GetListAsync(
            x => x.IsEnabled && x.DeviceType == DeviceType.PLC, ct);

        foreach (var device in devices)
        {
            try
            {
                var mappings = await _ioMappings.GetListAsync(
                    x => x.NetworkDeviceId == device.Id, ct);

                var mappingArray = mappings
                    .OrderBy(x => x.SortOrder)
                    .ToArray();

                var readCount = mappingArray
                    .Where(x => x.Direction == "Read")
                    .Sum(x => x.AddressCount);

                var writeCount = mappingArray
                    .Where(x => x.Direction == "Write")
                    .Sum(x => x.AddressCount);

                _dataStore.Register(device.Id, readCount, writeCount);

                var plcType = Enum.Parse<PlcType>(device.DeviceModel!, ignoreCase: true);
                var plcService = _plcServiceFactory.Create(plcType, device.DeviceName);
                _plcInstances[device.Id] = plcService;

                var signalInteraction = new SignalInteraction(
                    plcService, _dataStore, device, mappingArray, _logger);

                await signalInteraction.ConnectAsync();

                var tasks = new List<IPlcTask> { signalInteraction };

                if (_taskFactories.TryGetValue(device.Id, out var factory))
                    tasks.AddRange(factory(plcService, mappingArray));

                _plcTasks[device.Id] = tasks;

                foreach (var task in tasks)
                    await task.StartAsync(_cts.Token);

                _logger.Info($"[{device.DeviceName}] 初始化完成，启动 {tasks.Count} 个Task");
            }
            catch (Exception ex)
            {
                _logger.Error($"[{device.DeviceName}] 初始化失败: {ex.Message}");
            }
        }
    }

    public async Task ReloadAsync(int networkDeviceId, CancellationToken ct = default)
    {
        if (_plcTasks.ContainsKey(networkDeviceId))
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            _plcTasks.Remove(networkDeviceId);
        }

        if (_plcInstances.TryGetValue(networkDeviceId, out var oldPlc))
        {
            oldPlc.Disconnect();
            oldPlc.Dispose();
            _plcInstances.Remove(networkDeviceId);
        }

        var device = await _networkDevices.GetByIdAsync(networkDeviceId, ct);
        if (device is null || !device.IsEnabled) return;

        var mappings = await _ioMappings.GetListAsync(
            x => x.NetworkDeviceId == networkDeviceId, ct);

        var mappingArray = mappings.OrderBy(x => x.SortOrder).ToArray();

        var readCount = mappingArray.Where(x => x.Direction == "Read").Sum(x => x.AddressCount);
        var writeCount = mappingArray.Where(x => x.Direction == "Write").Sum(x => x.AddressCount);
        _dataStore.Register(networkDeviceId, readCount, writeCount);

        var plcType = Enum.Parse<PlcType>(device.DeviceModel!, ignoreCase: true);
        var plcService = _plcServiceFactory.Create(plcType, device.DeviceName);
        _plcInstances[networkDeviceId] = plcService;

        var signalInteraction = new SignalInteraction(
            plcService, _dataStore, device, mappingArray, _logger);

        await signalInteraction.ConnectAsync();

        var tasks = new List<IPlcTask> { signalInteraction };
        if (_taskFactories.TryGetValue(networkDeviceId, out var factory))
            tasks.AddRange(factory(plcService, mappingArray));

        _plcTasks[networkDeviceId] = tasks;

        foreach (var task in tasks)
            await task.StartAsync(_cts.Token);

        _logger.Info($"[{device.DeviceName}] 热重载完成");
    }

    public IPlcService? GetPlc(int networkDeviceId)
        => _plcInstances.TryGetValue(networkDeviceId, out var plc) ? plc : null;

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var plc in _plcInstances.Values)
        {
            plc.Disconnect();
            plc.Dispose();
        }
        _plcInstances.Clear();
        _plcTasks.Clear();
    }
}