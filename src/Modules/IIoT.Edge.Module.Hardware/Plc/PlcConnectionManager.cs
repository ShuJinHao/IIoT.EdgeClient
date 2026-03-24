// 路径：src/Modules/IIoT.Edge.Module.Hardware/Plc/PlcConnectionManager.cs
using IIoT.Edge.Common.Enums;
using IIoT.Edge.Common.Repository;
using IIoT.Edge.Contracts;
using IIoT.Edge.Contracts.Plc;
using IIoT.Edge.Contracts.Plc.Factory;
using IIoT.Edge.Contracts.Plc.Store;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Tasks.Context;

namespace IIoT.Edge.Module.Hardware.Plc;

public class PlcConnectionManager : IDisposable
{
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly IPlcDataStore _dataStore;
    private readonly IPlcServiceFactory _plcServiceFactory;
    private readonly ProductionContextStore _contextStore;
    private readonly ILogService _logger;

    private readonly Dictionary<int, IPlcService> _plcInstances = new();
    private readonly Dictionary<int, List<IPlcTask>> _plcTasks = new();
    private readonly Dictionary<int, CancellationTokenSource> _deviceCtsMap = new();

    /// <summary>
    /// 任务工厂注册表（按设备名称注册）
    /// 工厂参数只有 buffer + context，其他依赖任务自己取
    /// </summary>
    private readonly Dictionary<string, Func<IPlcBuffer, ProductionContext, List<IPlcTask>>> _taskFactories = new();

    public PlcConnectionManager(
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<IoMappingEntity> ioMappings,
        IPlcDataStore dataStore,
        IPlcServiceFactory plcServiceFactory,
        ProductionContextStore contextStore,
        ILogService logger)
    {
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _dataStore = dataStore;
        _plcServiceFactory = plcServiceFactory;
        _contextStore = contextStore;
        _logger = logger;
    }

    /// <summary>
    /// 按设备名称注册任务工厂
    /// 工厂只接收 buffer 和 context，任务需要的其他依赖自己从外部获取
    /// </summary>
    public void RegisterTasks(
        string deviceName,
        Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
    {
        _taskFactories[deviceName] = factory;
    }

    /// <summary>
    /// 获取指定设备的PLC通信实例（任务需要直读数据时调用）
    /// </summary>
    public IPlcService? GetPlc(int networkDeviceId)
        => _plcInstances.TryGetValue(networkDeviceId, out var plc) ? plc : null;

    /// <summary>
    /// 获取指定设备的生产上下文
    /// </summary>
    public ProductionContext? GetContext(int networkDeviceId)
        => _contextStore.GetOrCreate(networkDeviceId);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var devices = await _networkDevices.GetListAsync(
            x => x.IsEnabled && x.DeviceType == DeviceType.PLC, ct);

        foreach (var device in devices)
        {
            try
            {
                await InitializeDeviceAsync(device, ct);
            }
            catch (Exception ex)
            {
                _logger.Error($"[{device.DeviceName}] 初始化失败: {ex.Message}");
            }
        }
    }

    private async Task InitializeDeviceAsync(NetworkDeviceEntity device, CancellationToken ct)
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

        // 注册Buffer
        _dataStore.Register(device.Id, readCount, writeCount);
        var buffer = _dataStore.GetBuffer(device.Id);

        // 获取或恢复该设备的生产上下文
        var context = _contextStore.GetOrCreate(device.Id, device.DeviceName);

        // 创建PLC通信实例
        var plcType = Enum.Parse<PlcType>(device.DeviceModel!, ignoreCase: true);
        var plcService = _plcServiceFactory.Create(plcType, device.DeviceName);
        _plcInstances[device.Id] = plcService;

        // 该设备独立CTS
        var deviceCts = new CancellationTokenSource();
        _deviceCtsMap[device.Id] = deviceCts;

        // SignalInteraction（信号搬运）
        var signalInteraction = new SignalInteraction(
            plcService, _dataStore, device, mappingArray, _logger);

        await signalInteraction.ConnectAsync();

        var tasks = new List<IPlcTask> { signalInteraction };

        // 按设备名称匹配任务工厂
        if (buffer is not null
            && _taskFactories.TryGetValue(device.DeviceName, out var factory))
        {
            // buffer 是 IPlcBufferTransport，传给工厂时自动向上转为 IPlcBuffer
            tasks.AddRange(factory(buffer, context));
        }

        _plcTasks[device.Id] = tasks;

        foreach (var task in tasks)
            await task.StartAsync(deviceCts.Token);

        _logger.Info($"[{device.DeviceName}] 初始化完成，启动 {tasks.Count} 个Task" +
            $"（上下文步骤: {string.Join(", ", context.StepStates.Select(kv => $"{kv.Key}={kv.Value}"))})");
    }

    public async Task ReloadAsync(string deviceName, CancellationToken ct = default)
    {
        // 先通过名称查设备
        var device = (await _networkDevices.GetListAsync(
            x => x.DeviceName == deviceName, ct)).FirstOrDefault();

        if (device is null) return;

        var deviceId = device.Id;

        // 只取消该设备的任务
        if (_deviceCtsMap.TryGetValue(deviceId, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
            _deviceCtsMap.Remove(deviceId);
        }

        if (_plcTasks.ContainsKey(deviceId))
            _plcTasks.Remove(deviceId);

        if (_plcInstances.TryGetValue(deviceId, out var oldPlc))
        {
            oldPlc.Disconnect();
            oldPlc.Dispose();
            _plcInstances.Remove(deviceId);
        }

        if (!device.IsEnabled) return;

        await InitializeDeviceAsync(device, ct);
        _logger.Info($"[{device.DeviceName}] 热重载完成（上下文已保留）");
    }

    public void Dispose()
    {
        _contextStore.SaveToFile();

        foreach (var cts in _deviceCtsMap.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _deviceCtsMap.Clear();

        foreach (var plc in _plcInstances.Values)
        {
            plc.Disconnect();
            plc.Dispose();
        }
        _plcInstances.Clear();
        _plcTasks.Clear();
    }
}