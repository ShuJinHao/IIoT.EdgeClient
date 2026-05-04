using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;

namespace IIoT.Edge.Runtime.Base;

/// <summary>
/// PLC IO 扫描任务基类，统一承载连接恢复、读写合并、缓冲区搬运和中文运行日志。
/// </summary>
public abstract class PlcIoScanTaskBase : IPlcIoScanTask
{
    private const int TaskLoopInterval = 10;
    private const int DisconnectLogIntervalSeconds = 30;

    private readonly IPlcService _plcService;
    private readonly IPlcDataStore _dataStore;
    private readonly ILogService _logger;
    private readonly PlcIoScanDevice _device;
    private readonly PlcIoScanMapping[] _readMappings;
    private readonly PlcIoScanMapping[] _writeMappings;
    private readonly bool _canMergeRead;
    private readonly string? _mergedReadAddress;
    private readonly ushort _mergedReadCount;
    private readonly bool _canMergeWrite;
    private readonly string? _mergedWriteAddress;
    private readonly ushort _mergedWriteCount;
    private int _retryCount;
    private DateTime _lastDisconnectLogTime = DateTime.MinValue;

    protected PlcIoScanTaskBase(
        IPlcService plcService,
        IPlcDataStore dataStore,
        PlcIoScanDevice device,
        IEnumerable<PlcIoScanMapping> mappings,
        ILogService logger)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var mappingArray = mappings?.OrderBy(static x => x.SortOrder).ToArray()
            ?? throw new ArgumentNullException(nameof(mappings));
        _readMappings = mappingArray.Where(static x => x.IsRead).ToArray();
        _writeMappings = mappingArray.Where(static x => x.IsWrite).ToArray();
        (_canMergeRead, _mergedReadAddress, _mergedReadCount) = TryMergeMappings(_readMappings);
        (_canMergeWrite, _mergedWriteAddress, _mergedWriteCount) = TryMergeMappings(_writeMappings);
    }

    public string TaskName => $"PlcIoScan_{_device.DeviceName}";

    public bool IsConnected => _plcService.IsConnected;

    protected int DeviceId => _device.DeviceId;

    protected string DeviceName => _device.DeviceName;

    public async Task ConnectAsync()
    {
        try
        {
            _plcService.Init(_device.IpAddress, _device.Port);
            var connected = await _plcService.ConnectAsync().ConfigureAwait(false);
            if (connected)
            {
                MarkConnected();
                if (_retryCount > 0 || _lastDisconnectLogTime != DateTime.MinValue)
                {
                    _logger.Info($"[{_device.DeviceName}] PLC 连接已恢复。");
                    _lastDisconnectLogTime = DateTime.MinValue;
                    _retryCount = 0;
                }

                return;
            }

            _retryCount++;
            MarkDisconnected("PLC 连接失败。");
            if (ShouldLogDisconnect())
            {
                _logger.Warn($"[{_device.DeviceName}] PLC 连接失败，进入退避重试。");
            }
        }
        catch (Exception ex)
        {
            _retryCount++;
            MarkDisconnected(ex.Message);
            if (ShouldLogDisconnect())
            {
                _logger.Error($"[{_device.DeviceName}] PLC 连接异常：{ex.Message}");
            }
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await Task.Factory.StartNew(
            () => TaskCoreAsync(ct),
            ct,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap().ConfigureAwait(false);
    }

    public Task ExecuteOneCycleAsync()
        => ExecuteOneCycleAsync(CancellationToken.None);

    public async Task ExecuteOneCycleAsync(CancellationToken ct)
    {
        if (!_plcService.IsConnected)
        {
            await ConnectAsync().ConfigureAwait(false);
            if (!_plcService.IsConnected)
            {
                await Task.Delay(GetBackoffDelay(), ct).ConfigureAwait(false);
                return;
            }
        }

        var buffer = _dataStore.GetBuffer(_device.DeviceId);
        if (buffer is null)
        {
            return;
        }

        await ReadPlcToBufferAsync(buffer).ConfigureAwait(false);
        await WriteBufferToPlcAsync(buffer).ConfigureAwait(false);
    }

    protected virtual void MarkConnected()
    {
    }

    protected virtual void MarkDisconnected(string reason)
    {
    }

    private async Task TaskCoreAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ExecuteOneCycleAsync(ct).ConfigureAwait(false);
                await Task.Delay(TaskLoopInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _retryCount++;
                if (ShouldLogDisconnect())
                {
                    _logger.Error($"[{_device.DeviceName}] PLC IO 扫描循环异常：{ex.Message}");
                }

                await Task.Delay(GetBackoffDelay(), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadPlcToBufferAsync(IPlcBufferTransport buffer)
    {
        try
        {
            ushort[] allReadData;
            if (_canMergeRead && _mergedReadAddress is not null)
            {
                var data = await _plcService
                    .ReadDataAsync<ushort>(_mergedReadAddress, _mergedReadCount)
                    .ConfigureAwait(false);
                allReadData = data.ToArray();
            }
            else
            {
                var list = new List<ushort>();
                foreach (var mapping in _readMappings)
                {
                    var data = await _plcService
                        .ReadDataAsync<ushort>(mapping.PlcAddress, (ushort)mapping.AddressCount)
                        .ConfigureAwait(false);
                    list.AddRange(data);
                }

                allReadData = list.ToArray();
            }

            buffer.UpdateReadBuffer(allReadData);
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect())
            {
                _logger.Error($"[{_device.DeviceName}] PLC 读取失败：{ex.Message}");
            }

            _plcService.Disconnect();
            MarkDisconnected($"PLC 读取失败：{ex.Message}");
            throw new InvalidOperationException("PLC 读取链路失败，连接已重置。", ex);
        }
    }

    private async Task WriteBufferToPlcAsync(IPlcBufferTransport buffer)
    {
        try
        {
            var writeData = buffer.GetWriteBuffer();
            if (_canMergeWrite && _mergedWriteAddress is not null)
            {
                await _plcService.WriteDataAsync(_mergedWriteAddress, writeData.ToList()).ConfigureAwait(false);
                return;
            }

            var writeOffset = 0;
            foreach (var mapping in _writeMappings)
            {
                var count = mapping.AddressCount;
                var segment = new ushort[count];
                Array.Copy(writeData, writeOffset, segment, 0, count);
                await _plcService.WriteDataAsync(mapping.PlcAddress, segment.ToList()).ConfigureAwait(false);
                writeOffset += count;
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect())
            {
                _logger.Error($"[{_device.DeviceName}] PLC 写入失败：{ex.Message}");
            }

            _plcService.Disconnect();
            MarkDisconnected($"PLC 写入失败：{ex.Message}");
            throw new InvalidOperationException("PLC 写入链路失败，连接已重置。", ex);
        }
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
        if ((now - _lastDisconnectLogTime).TotalSeconds < DisconnectLogIntervalSeconds)
        {
            return false;
        }

        _lastDisconnectLogTime = now;
        return true;
    }

    private static (bool canMerge, string? startAddress, ushort totalCount) TryMergeMappings(IReadOnlyList<PlcIoScanMapping> mappings)
    {
        if (mappings.Count == 0)
        {
            return (false, null, 0);
        }

        if (mappings.Count == 1)
        {
            return (true, mappings[0].PlcAddress, (ushort)mappings[0].AddressCount);
        }

        var firstNum = ParseAddressNumber(mappings[0].PlcAddress);
        if (firstNum < 0)
        {
            return (false, null, 0);
        }

        var expectedNext = firstNum + mappings[0].AddressCount;
        for (var index = 1; index < mappings.Count; index++)
        {
            var num = ParseAddressNumber(mappings[index].PlcAddress);
            if (num != expectedNext)
            {
                return (false, null, 0);
            }

            expectedNext = num + mappings[index].AddressCount;
        }

        return (true, mappings[0].PlcAddress, (ushort)mappings.Sum(static x => x.AddressCount));
    }

    private static int ParseAddressNumber(string address)
    {
        var index = address.Length - 1;
        while (index >= 0 && char.IsDigit(address[index]))
        {
            index--;
        }

        if (index == address.Length - 1)
        {
            return -1;
        }

        return int.TryParse(address[(index + 1)..], out var num) ? num : -1;
    }
}

/// <summary>
/// PLC IO 扫描任务绑定的设备信息。
/// </summary>
public sealed record PlcIoScanDevice(int DeviceId, string DeviceName, string IpAddress, int Port);

/// <summary>
/// PLC IO 扫描任务使用的数据库映射快照。
/// </summary>
public sealed record PlcIoScanMapping(string PlcAddress, int AddressCount, string Direction, int SortOrder)
{
    public bool IsRead => string.Equals(Direction, "Read", StringComparison.OrdinalIgnoreCase);

    public bool IsWrite => string.Equals(Direction, "Write", StringComparison.OrdinalIgnoreCase);
}
