using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Runtime.Plc;

namespace IIoT.Edge.Runtime.Base;

/// <summary>
/// PLC 信号交互扫描任务基类，只承载实时信号交互的循环读写。
/// </summary>
public abstract class PlcIoScanTaskBase : IPlcIoScanTask
{
    private const int DisconnectLogIntervalSeconds = 30;

    private readonly IPlcService _plcService;
    private readonly IPlcDataStore _dataStore;
    private readonly ILogService _logger;
    private readonly PlcIoScanDevice _device;
    private readonly int _loopIntervalMs;
    private readonly IReadOnlyList<PlcSignalBlock> _readBlocks;
    private readonly IReadOnlyList<PlcSignalBlock> _writeBlocks;
    private int _retryCount;
    private DateTime _lastDisconnectLogTime = DateTime.MinValue;

    protected PlcIoScanTaskBase(
        IPlcService plcService,
        IPlcDataStore dataStore,
        PlcIoScanDevice device,
        IEnumerable<PlcIoScanMapping> mappings,
        ILogService logger,
        IPlcSignalBlockPlanner signalBlockPlanner,
        PlcIoRuntimePolicy? runtimePolicy = null)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(signalBlockPlanner);

        var policy = runtimePolicy ?? PlcIoRuntimePolicy.Default;
        _loopIntervalMs = policy.NormalizeLoopInterval();

        var interactionMappings = (mappings ?? throw new ArgumentNullException(nameof(mappings)))
            .Where(static mapping => string.Equals(
                mapping.Category,
                IoMappingOptionCatalog.CategoryInteraction,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(static mapping => mapping.SortOrder)
            .ToArray();

        _readBlocks = signalBlockPlanner.Plan(
            interactionMappings.Where(static x => x.IsRead).ToArray(),
            policy.NormalizeMaxBlockWordCount(),
            policy.WriteGapPolicy,
            isWrite: false);
        _writeBlocks = signalBlockPlanner.Plan(
            interactionMappings.Where(static x => x.IsWrite).ToArray(),
            policy.NormalizeMaxBlockWordCount(),
            policy.WriteGapPolicy,
            isWrite: true);
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
                await Task.Delay(_loopIntervalMs, ct).ConfigureAwait(false);
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
                    _logger.Error($"[{_device.DeviceName}] PLC 信号交互循环异常：{ex.Message}");
                }

                await Task.Delay(GetBackoffDelay(), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadPlcToBufferAsync(IPlcBufferTransport buffer)
    {
        try
        {
            foreach (var block in _readBlocks)
            {
                var data = await _plcService
                    .ReadDataAsync<ushort>(block.StartAddress, (ushort)block.WordCount)
                    .ConfigureAwait(false);
                var words = data.ToArray();

                foreach (var item in block.Items)
                {
                    buffer.UpdateReadSignal(
                        item.Mapping.SignalKey,
                        SliceWords(words, item.Offset, item.Mapping.AddressCount));
                }
            }
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
            foreach (var block in _writeBlocks)
            {
                var blockWords = new ushort[block.WordCount];
                foreach (var item in block.Items)
                {
                    if (!buffer.TryGetWriteWords(item.Mapping.SignalKey, out var signalWords))
                    {
                        continue;
                    }

                    for (var index = 0; index < Math.Min(signalWords.Length, item.Mapping.AddressCount); index++)
                    {
                        var blockIndex = item.Offset + index;
                        if (blockIndex >= 0 && blockIndex < blockWords.Length)
                        {
                            blockWords[blockIndex] = signalWords[index];
                        }
                    }
                }

                await _plcService.WriteDataAsync(block.StartAddress, blockWords.ToList()).ConfigureAwait(false);
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

    private static ushort[] SliceWords(IReadOnlyList<ushort> words, int offset, int count)
    {
        var result = new ushort[Math.Max(1, count)];
        for (var index = 0; index < result.Length; index++)
        {
            var sourceIndex = offset + index;
            result[index] = sourceIndex >= 0 && sourceIndex < words.Count ? words[sourceIndex] : (ushort)0;
        }

        return result;
    }
}

/// <summary>
/// PLC IO 扫描任务绑定的设备信息。
/// </summary>
public sealed record PlcIoScanDevice(int DeviceId, string DeviceName, string IpAddress, int Port);

/// <summary>
/// PLC IO 扫描任务使用的数据库映射快照。
/// </summary>
public sealed record PlcIoScanMapping(
    string SignalKey,
    string PlcAddress,
    int AddressCount,
    string DataType,
    string Direction,
    string Category,
    int SortOrder)
{
    public bool IsRead => string.Equals(Direction, "Read", StringComparison.OrdinalIgnoreCase);

    public bool IsWrite => string.Equals(Direction, "Write", StringComparison.OrdinalIgnoreCase);
}
