using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Signals;

public class SignalInteraction : ISignalInteraction
{
    private readonly IPlcService _plcService;
    private readonly IPlcDataStore _dataStore;
    private readonly NetworkDeviceEntity _deviceConfig;
    private readonly ILogService _logger;
    private readonly PlcConnectionStatusStore? _statusStore;
    private readonly IoMappingEntity[] _readMappings;
    private readonly IoMappingEntity[] _writeMappings;
    private readonly bool _canMergeRead;
    private readonly string? _mergedReadAddress;
    private readonly ushort _mergedReadCount;
    private readonly bool _canMergeWrite;
    private readonly string? _mergedWriteAddress;
    private readonly ushort _mergedWriteCount;
    private const int TaskLoopInterval = 10;
    private const int DisconnectLogIntervalSeconds = 30;
    private int _retryCount;
    private DateTime _lastDisconnectLogTime = DateTime.MinValue;

    public string TaskName => $"SignalInteraction_{_deviceConfig.DeviceName}";
    public bool IsConnected => _plcService.IsConnected;

    public SignalInteraction(
        IPlcService plcService,
        IPlcDataStore dataStore,
        NetworkDeviceEntity deviceConfig,
        IoMappingEntity[] ioMappings,
        ILogService logger,
        PlcConnectionStatusStore? statusStore = null)
    {
        _plcService = plcService;
        _dataStore = dataStore;
        _deviceConfig = deviceConfig;
        _logger = logger;
        _statusStore = statusStore;
        _readMappings = ioMappings.Where(x => x.Direction == "Read").OrderBy(x => x.SortOrder).ToArray();
        _writeMappings = ioMappings.Where(x => x.Direction == "Write").OrderBy(x => x.SortOrder).ToArray();
        (_canMergeRead, _mergedReadAddress, _mergedReadCount) = TryMergeMappings(_readMappings);
        (_canMergeWrite, _mergedWriteAddress, _mergedWriteCount) = TryMergeMappings(_writeMappings);
    }

    private int GetBackoffDelay()
    {
        if (_retryCount <= 3)
        {
            return 50;
        }

        if (_retryCount <= 10)
        {
            return 2000;
        }

        if (_retryCount <= 30)
        {
            return 10000;
        }

        return 30000;
    }

    private bool ShouldLogDisconnect()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastDisconnectLogTime).TotalSeconds >= DisconnectLogIntervalSeconds)
        {
            _lastDisconnectLogTime = now;
            return true;
        }

        return false;
    }

    private static (bool canMerge, string? startAddress, ushort totalCount) TryMergeMappings(IoMappingEntity[] mappings)
    {
        if (mappings.Length == 0) return (false, null, 0);
        if (mappings.Length == 1) return (true, mappings[0].PlcAddress, (ushort)mappings[0].AddressCount);

        var firstNum = ParseAddressNumber(mappings[0].PlcAddress);
        if (firstNum < 0) return (false, null, 0);

        var expectedNext = firstNum + mappings[0].AddressCount;
        for (var i = 1; i < mappings.Length; i++)
        {
            var num = ParseAddressNumber(mappings[i].PlcAddress);
            if (num != expectedNext) return (false, null, 0);
            expectedNext = num + mappings[i].AddressCount;
        }

        return (true, mappings[0].PlcAddress, (ushort)mappings.Sum(x => x.AddressCount));
    }

    private static int ParseAddressNumber(string address)
    {
        var i = address.Length - 1;
        while (i >= 0 && char.IsDigit(address[i])) i--;
        if (i == address.Length - 1) return -1;
        return int.TryParse(address[(i + 1)..], out var num) ? num : -1;
    }

    public async Task ConnectAsync()
    {
        try
        {
            _plcService.Init(_deviceConfig.IpAddress, _deviceConfig.Port1);
            var result = await _plcService.ConnectAsync().ConfigureAwait(false);
            if (result)
            {
                _statusStore?.MarkConnected(_deviceConfig.Id, _deviceConfig.DeviceName);
                if (_retryCount > 0 || _lastDisconnectLogTime != DateTime.MinValue)
                {
                    _logger.Info($"[{_deviceConfig.DeviceName}] Connected successfully.");
                    _lastDisconnectLogTime = DateTime.MinValue;
                    _retryCount = 0;
                }
            }
            else
            {
                _retryCount++;
                _statusStore?.MarkDisconnected(_deviceConfig.Id, _deviceConfig.DeviceName, "Connect failed.");
                if (ShouldLogDisconnect()) _logger.Warn($"[{_deviceConfig.DeviceName}] Connect failed. Entering backoff retry.");
            }
        }
        catch (Exception ex)
        {
            _retryCount++;
            _statusStore?.MarkDisconnected(_deviceConfig.Id, _deviceConfig.DeviceName, ex.Message);
            if (ShouldLogDisconnect()) _logger.Error($"[{_deviceConfig.DeviceName}] Connect exception: {ex.Message}");
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await Task.Factory.StartNew(() => TaskCoreAsync(ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap().ConfigureAwait(false);
    }

    private async Task TaskCoreAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ExecuteOneCycleAsync().ConfigureAwait(false);
                await Task.Delay(TaskLoopInterval, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _retryCount++;
                if (ShouldLogDisconnect()) _logger.Error($"[{_deviceConfig.DeviceName}] Loop exception: {ex.Message}");
                await Task.Delay(GetBackoffDelay(), ct).ConfigureAwait(false);
            }
        }
    }

    internal Task ExecuteOneCycleAsync() => DoCoreAsync();

    private async Task DoCoreAsync()
    {
        if (!_plcService.IsConnected)
        {
            await ConnectAsync().ConfigureAwait(false);
            if (!_plcService.IsConnected)
            {
                await Task.Delay(GetBackoffDelay()).ConfigureAwait(false);
                return;
            }
        }

        var buffer = _dataStore.GetBuffer(_deviceConfig.Id);
        if (buffer is null) return;

        try
        {
            ushort[] allReadData;
            if (_canMergeRead && _mergedReadAddress is not null)
            {
                var data = await _plcService.ReadDataAsync<ushort>(_mergedReadAddress, _mergedReadCount).ConfigureAwait(false);
                allReadData = data.ToArray();
            }
            else
            {
                var list = new List<ushort>();
                for (var i = 0; i < _readMappings.Length; i++)
                {
                    var data = await _plcService.ReadDataAsync<ushort>(_readMappings[i].PlcAddress, (ushort)_readMappings[i].AddressCount).ConfigureAwait(false);
                    list.AddRange(data);
                }

                allReadData = list.ToArray();
            }

            buffer.UpdateReadBuffer(allReadData);
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect()) _logger.Error($"[{_deviceConfig.DeviceName}] Read failed: {ex.Message}");
            _plcService.Disconnect();
            _statusStore?.MarkDisconnected(_deviceConfig.Id, _deviceConfig.DeviceName, $"Read failed: {ex.Message}");
            throw new Exception("Read pipeline failed and the PLC connection was reset.");
        }

        try
        {
            var writeData = buffer.GetWriteBuffer();
            if (_canMergeWrite && _mergedWriteAddress is not null)
            {
                await _plcService.WriteDataAsync(_mergedWriteAddress, writeData.ToList()).ConfigureAwait(false);
            }
            else
            {
                var writeOffset = 0;
                for (var i = 0; i < _writeMappings.Length; i++)
                {
                    var count = _writeMappings[i].AddressCount;
                    var segment = new ushort[count];
                    Array.Copy(writeData, writeOffset, segment, 0, count);
                    await _plcService.WriteDataAsync(_writeMappings[i].PlcAddress, segment.ToList()).ConfigureAwait(false);
                    writeOffset += count;
                }
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect()) _logger.Error($"[{_deviceConfig.DeviceName}] Write failed: {ex.Message}");
            _plcService.Disconnect();
            _statusStore?.MarkDisconnected(_deviceConfig.Id, _deviceConfig.DeviceName, $"Write failed: {ex.Message}");
            throw new Exception("Write pipeline failed and the PLC connection was reset.");
        }
    }
}
