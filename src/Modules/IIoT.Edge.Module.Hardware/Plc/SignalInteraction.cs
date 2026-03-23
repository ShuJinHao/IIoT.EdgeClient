using IIoT.Edge.Contracts;
using IIoT.Edge.Contracts.Plc;
using IIoT.Edge.Contracts.Plc.Store;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Module.Hardware.Plc;

public class SignalInteraction : ISignalInteraction
{
    private readonly IPlcService _plcService;
    private readonly IPlcDataStore _dataStore;
    private readonly NetworkDeviceEntity _deviceConfig;
    private readonly ILogService _logger;

    private readonly IoMappingEntity[] _readMappings;
    private readonly IoMappingEntity[] _writeMappings;

    private const int TaskLoopInterval = 10;
    private const int ReconnectInterval = 1000;

    public string TaskName => $"SignalInteraction_{_deviceConfig.DeviceName}";
    public bool IsConnected => _plcService.IsConnected;

    public SignalInteraction(
        IPlcService plcService,
        IPlcDataStore dataStore,
        NetworkDeviceEntity deviceConfig,
        IoMappingEntity[] ioMappings,
        ILogService logger)
    {
        _plcService = plcService;
        _dataStore = dataStore;
        _deviceConfig = deviceConfig;
        _logger = logger;

        // 启动时分好缓存，循环里直接用
        _readMappings = ioMappings
            .Where(x => x.Direction == "Read")
            .OrderBy(x => x.SortOrder)
            .ToArray();

        _writeMappings = ioMappings
            .Where(x => x.Direction == "Write")
            .OrderBy(x => x.SortOrder)
            .ToArray();
    }

    public async Task ConnectAsync()
    {
        try
        {
            _plcService.Init(_deviceConfig.IpAddress, _deviceConfig.Port1);
            var result = await _plcService.ConnectAsync();
            if (!result)
                _logger.Warn($"[{_deviceConfig.DeviceName}] 连接失败，等待轮询重连");
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceConfig.DeviceName}] 连接异常: {ex.Message}");
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await Task.Factory.StartNew(
            () => TaskCoreAsync(ct),
            ct,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    private async Task TaskCoreAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DoCoreAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"[{_deviceConfig.DeviceName}] 任务循环异常: {ex.Message}");
                await Task.Delay(ReconnectInterval, ct);
            }

            await Task.Delay(TaskLoopInterval, ct);
        }
    }

    private async Task DoCoreAsync()
    {
        if (!_plcService.IsConnected)
        {
            _logger.Warn($"[{_deviceConfig.DeviceName}] 连接断开，重连中...");
            await ConnectAsync();
            return;
        }

        var buffer = _dataStore.GetBuffer(_deviceConfig.Id);
        if (buffer is null) return;

        try
        {
            // 批量读取，按地址段顺序合并到ReadBuffer
            var allReadData = new List<ushort>();
            for (int i = 0; i < _readMappings.Length; i++)
            {
                var data = await _plcService.ReadDataAsync<ushort>(
                    _readMappings[i].PlcAddress,
                    (ushort)_readMappings[i].AddressCount);
                allReadData.AddRange(data);
            }
            buffer.UpdateReadBuffer(allReadData.ToArray());

            // 批量写入，从WriteBuffer按地址段分段写出
            var writeBuffer = buffer.GetWriteBuffer();
            int writeOffset = 0;
            for (int i = 0; i < _writeMappings.Length; i++)
            {
                var count = _writeMappings[i].AddressCount;
                var segment = new ushort[count];
                Array.Copy(writeBuffer, writeOffset, segment, 0, count);
                await _plcService.WriteDataAsync(_writeMappings[i].PlcAddress, segment.ToList());
                writeOffset += count;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceConfig.DeviceName}] IO交互异常: {ex.Message}");
            _plcService.Disconnect();
        }
    }
}