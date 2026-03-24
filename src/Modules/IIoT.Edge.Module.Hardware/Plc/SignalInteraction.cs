// 路径：src/Modules/IIoT.Edge.Module.Hardware/Plc/SignalInteraction.cs
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

    private readonly bool _canMergeRead;
    private readonly string? _mergedReadAddress;
    private readonly ushort _mergedReadCount;

    private readonly bool _canMergeWrite;
    private readonly string? _mergedWriteAddress;
    private readonly ushort _mergedWriteCount;

    private const int TaskLoopInterval = 10;
    private const int ReconnectInterval = 1000;

    // 断连日志频率控制
    private DateTime _lastDisconnectLogTime = DateTime.MinValue;
    private const int DisconnectLogIntervalSeconds = 30;

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

        _readMappings = ioMappings
            .Where(x => x.Direction == "Read")
            .OrderBy(x => x.SortOrder)
            .ToArray();

        _writeMappings = ioMappings
            .Where(x => x.Direction == "Write")
            .OrderBy(x => x.SortOrder)
            .ToArray();

        (_canMergeRead, _mergedReadAddress, _mergedReadCount)
            = TryMergeMappings(_readMappings);
        (_canMergeWrite, _mergedWriteAddress, _mergedWriteCount)
            = TryMergeMappings(_writeMappings);
    }

    /// <summary>
    /// 断连日志频率控制：同一断连周期内，30秒只输出一次
    /// </summary>
    private bool ShouldLogDisconnect()
    {
        var now = DateTime.Now;
        if ((now - _lastDisconnectLogTime).TotalSeconds >= DisconnectLogIntervalSeconds)
        {
            _lastDisconnectLogTime = now;
            return true;
        }
        return false;
    }

    private static (bool canMerge, string? startAddress, ushort totalCount)
        TryMergeMappings(IoMappingEntity[] mappings)
    {
        if (mappings.Length == 0)
            return (false, null, 0);

        if (mappings.Length == 1)
            return (true, mappings[0].PlcAddress,
                (ushort)mappings[0].AddressCount);

        var firstNum = ParseAddressNumber(mappings[0].PlcAddress);
        if (firstNum < 0)
            return (false, null, 0);

        int expectedNext = firstNum + mappings[0].AddressCount;
        for (int i = 1; i < mappings.Length; i++)
        {
            var num = ParseAddressNumber(mappings[i].PlcAddress);
            if (num != expectedNext)
                return (false, null, 0);
            expectedNext = num + mappings[i].AddressCount;
        }

        var totalCount = (ushort)mappings.Sum(x => x.AddressCount);
        return (true, mappings[0].PlcAddress, totalCount);
    }

    private static int ParseAddressNumber(string address)
    {
        int i = address.Length - 1;
        while (i >= 0 && char.IsDigit(address[i])) i--;
        if (i == address.Length - 1) return -1;
        return int.TryParse(address[(i + 1)..], out var num) ? num : -1;
    }

    public async Task ConnectAsync()
    {
        try
        {
            _plcService.Init(_deviceConfig.IpAddress, _deviceConfig.Port1);
            var result = await _plcService.ConnectAsync();
            if (!result)
            {
                if (ShouldLogDisconnect())
                    _logger.Warn($"[{_deviceConfig.DeviceName}] 连接失败，等待轮询重连");
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect())
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
                if (ShouldLogDisconnect())
                    _logger.Error(
                        $"[{_deviceConfig.DeviceName}] 任务循环异常: {ex.Message}");
                await Task.Delay(ReconnectInterval, ct);
            }

            await Task.Delay(TaskLoopInterval, ct);
        }
    }

    private async Task DoCoreAsync()
    {
        if (!_plcService.IsConnected)
        {
            if (ShouldLogDisconnect())
                _logger.Warn($"[{_deviceConfig.DeviceName}] 连接断开，重连中...");

            await ConnectAsync();
            await Task.Delay(ReconnectInterval);
            return;
        }

        // 重连成功，输出一条成功日志并重置计时器
        if (_lastDisconnectLogTime != DateTime.MinValue)
        {
            _logger.Info($"[{_deviceConfig.DeviceName}] 重连成功");
            _lastDisconnectLogTime = DateTime.MinValue;
        }

        IPlcBufferTransport? buffer = _dataStore.GetBuffer(_deviceConfig.Id);
        if (buffer is null) return;

        // ========== 读取 ==========
        try
        {
            ushort[] allReadData;

            if (_canMergeRead && _mergedReadAddress is not null)
            {
                var data = await _plcService.ReadDataAsync<ushort>(
                    _mergedReadAddress, _mergedReadCount);
                allReadData = data.ToArray();
            }
            else
            {
                var list = new List<ushort>();
                for (int i = 0; i < _readMappings.Length; i++)
                {
                    var data = await _plcService.ReadDataAsync<ushort>(
                        _readMappings[i].PlcAddress,
                        (ushort)_readMappings[i].AddressCount);
                    list.AddRange(data);
                }
                allReadData = list.ToArray();
            }

            buffer.UpdateReadBuffer(allReadData);
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect())
                _logger.Error(
                    $"[{_deviceConfig.DeviceName}] 读取异常: {ex.Message}");
            _plcService.Disconnect();
            return;
        }

        // ========== 写入 ==========
        try
        {
            var writeData = buffer.GetWriteBuffer();

            if (_canMergeWrite && _mergedWriteAddress is not null)
            {
                await _plcService.WriteDataAsync(
                    _mergedWriteAddress, writeData.ToList());
            }
            else
            {
                int writeOffset = 0;
                for (int i = 0; i < _writeMappings.Length; i++)
                {
                    var count = _writeMappings[i].AddressCount;
                    var segment = new ushort[count];
                    Array.Copy(writeData, writeOffset, segment, 0, count);
                    await _plcService.WriteDataAsync(
                        _writeMappings[i].PlcAddress, segment.ToList());
                    writeOffset += count;
                }
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect())
                _logger.Error(
                    $"[{_deviceConfig.DeviceName}] 写入异常: {ex.Message}");
            _plcService.Disconnect();
        }
    }
}