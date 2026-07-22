using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using System.Diagnostics;

namespace IIoT.Edge.Infrastructure.DeviceComm.Signals;

/// <summary>
/// PLC 信号交互扫描任务基类，只承载实时信号交互的循环读写。
/// </summary>
public abstract class PlcIoScanTaskBase : IPlcIoScanTask
{
    private const int DisconnectLogIntervalSeconds = 30;
    private const int MaxConnectTimeoutMs = 3000;

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

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = ClampConnectTimeout(_device.Endpoint);
        try
        {
            _plcService.Init(endpoint);
            MarkConnecting();
            var connected = await _plcService.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            if (connected)
            {
                return;
            }

            _retryCount++;
            MarkDisconnected("PLC 连接失败。");
            if (ShouldLogDisconnect())
            {
                _logger.Warn($"[{_device.DeviceName}] PLC 连接失败，进入退避重试。");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlcServiceQuarantinedException ex)
        {
            MarkRuntimeFault(ex.Message);
            _logger.Error($"[{_device.DeviceName}] PLC service 已隔离：{ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            await CloseHangingConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            _retryCount++;
            MarkDisconnected(ex.Message);
            if (ShouldLogDisconnect())
            {
                _logger.Error($"[{_device.DeviceName}] PLC 连接异常：{ex.Message}");
            }
        }
    }

    public Task StartAsync(CancellationToken ct)
        => TaskCoreAsync(ct);

    public async Task ExecuteOneCycleAsync(CancellationToken ct)
    {
        if (!_plcService.IsConnected)
        {
            await ConnectAsync(ct).ConfigureAwait(false);
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

        if (_readBlocks.Count == 0)
        {
            if (_writeBlocks.Count > 0)
            {
                const string reason = "PLC 缺少只读协议校验点位，禁止执行写入。";
                MarkRuntimeFault(reason);
                if (ShouldLogDisconnect())
                {
                    _logger.Warn($"[{_device.DeviceName}] {reason}");
                }
            }

            return;
        }

        var wasStableOnline = IsStableOnline();
        var cycleStopwatch = Stopwatch.StartNew();
        await ReadPlcToBufferAsync(buffer, updateBuffer: wasStableOnline, ct).ConfigureAwait(false);
        var isStableOnline = MarkProtocolSuccess(ToLatencyMs(cycleStopwatch.ElapsedMilliseconds));
        if (!wasStableOnline)
        {
            LogRecoveredIfNeeded(isStableOnline);
            return;
        }

        await WriteBufferToPlcAsync(buffer, ct).ConfigureAwait(false);
        LogRecoveredIfNeeded(isStableOnline);
    }

    protected virtual void MarkConnected(int? latencyMs)
    {
    }

    protected virtual bool MarkProtocolSuccess(int? latencyMs)
        => true;

    protected virtual bool IsStableOnline()
        => true;

    protected virtual void MarkRuntimeFault(string reason)
    {
    }

    protected virtual void MarkConnecting()
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (PlcServiceQuarantinedException ex)
            {
                MarkRuntimeFault(ex.Message);
                _logger.Error($"[{_device.DeviceName}] PLC service 已隔离，扫描任务已停止：{ex.Message}");
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

    private async Task ReadPlcToBufferAsync(
        IPlcBufferTransport buffer,
        bool updateBuffer,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var block in _readBlocks)
            {
                var data = await _plcService
                    .ReadDataAsync<ushort>(block.StartAddress, (ushort)block.WordCount, cancellationToken)
                    .ConfigureAwait(false);
                var words = data.ToArray();

                foreach (var item in block.Items)
                {
                    if (updateBuffer)
                    {
                        buffer.UpdateReadSignal(
                            item.Mapping.SignalKey,
                            SliceWords(words, item.Offset, item.Mapping.AddressCount));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlcServiceQuarantinedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect())
            {
                _logger.Error($"[{_device.DeviceName}] PLC 读取失败：{ex.Message}");
            }

            await _plcService.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            MarkDisconnected($"PLC 读取失败：{ex.Message}");
            throw new InvalidOperationException("PLC 读取链路失败，连接已重置。", ex);
        }
    }

    private void LogRecoveredIfNeeded(bool isStableOnline)
    {
        if (!isStableOnline || (_retryCount <= 0 && _lastDisconnectLogTime == DateTime.MinValue))
        {
            return;
        }

        _logger.Info($"[{_device.DeviceName}] PLC 连接已恢复。");
        _lastDisconnectLogTime = DateTime.MinValue;
        _retryCount = 0;
    }

    private async Task WriteBufferToPlcAsync(
        IPlcBufferTransport buffer,
        CancellationToken cancellationToken)
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

                await _plcService
                    .WriteDataAsync(block.StartAddress, blockWords.ToList(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlcServiceQuarantinedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect())
            {
                _logger.Error($"[{_device.DeviceName}] PLC 写入失败：{ex.Message}");
            }

            await _plcService.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            MarkDisconnected($"PLC 写入失败：{ex.Message}");
            throw new InvalidOperationException("PLC 写入链路失败，连接已重置。", ex);
        }
    }

    private int GetBackoffDelay()
    {
        if (_retryCount <= 3)
        {
            return 2000;
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

    private static int? ToLatencyMs(long? elapsedMilliseconds)
        => elapsedMilliseconds.HasValue
            ? (int)Math.Min(int.MaxValue, Math.Max(0, elapsedMilliseconds.Value))
            : null;

    private static PlcEndpoint ClampConnectTimeout(PlcEndpoint endpoint)
    {
        var timeoutMs = endpoint.ConnectTimeoutMs <= 0
            ? MaxConnectTimeoutMs
            : Math.Min(endpoint.ConnectTimeoutMs, MaxConnectTimeoutMs);

        return endpoint switch
        {
            TcpPlcEndpoint tcp => new TcpPlcEndpoint(tcp.Host, tcp.Port, timeoutMs, tcp.McFrameType),
            SerialPlcEndpoint serial => new SerialPlcEndpoint(
                serial.PortName,
                serial.BaudRate,
                serial.DataBits,
                serial.StopBits,
                serial.Parity,
                serial.SlaveId,
                timeoutMs),
            _ => endpoint
        };
    }

    private async Task CloseHangingConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _plcService.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PlcServiceQuarantinedException)
        {
            throw;
        }
        catch
        {
            // 连接失败后的主动关闭不能反向打断扫描循环的重试路径。
        }
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
public sealed record PlcIoScanDevice(int DeviceId, string DeviceName, PlcEndpoint Endpoint);

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
