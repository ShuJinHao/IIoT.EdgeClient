using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Signals;

/// <summary>
/// PLC 只读数据扫描任务，负责把单点读数据和连续读数据刷新到运行缓冲区。
/// </summary>
public sealed class PlcDataReadScanTask : IPlcTask
{
    private const int DisconnectLogIntervalSeconds = 30;

    private readonly IPlcService _plcService;
    private readonly IPlcDataStore _dataStore;
    private readonly NetworkDeviceEntity _device;
    private readonly ILogService _logger;
    private readonly PlcConnectionStatusStore? _statusStore;
    private readonly Func<CancellationToken, Task<int>> _dataReadLoopIntervalResolver;
    private readonly IReadOnlyList<PlcSignalBlock> _readBlocks;
    private int _retryCount;
    private DateTime _lastDisconnectLogTime = DateTime.MinValue;

    public PlcDataReadScanTask(
        IPlcService plcService,
        IPlcDataStore dataStore,
        NetworkDeviceEntity deviceConfig,
        IReadOnlyCollection<IoMappingEntity> ioMappings,
        ILogService logger,
        IPlcSignalBlockPlanner signalBlockPlanner,
        PlcConnectionStatusStore? statusStore = null,
        PlcIoRuntimePolicy? runtimePolicy = null,
        Func<CancellationToken, Task<int>>? dataReadLoopIntervalResolver = null)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _device = deviceConfig ?? throw new ArgumentNullException(nameof(deviceConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _statusStore = statusStore;
        ArgumentNullException.ThrowIfNull(signalBlockPlanner);

        var policy = runtimePolicy ?? PlcIoRuntimePolicy.Default;
        _dataReadLoopIntervalResolver = dataReadLoopIntervalResolver
            ?? (_ => Task.FromResult(policy.NormalizeDataReadLoopInterval()));

        var readMappings = (ioMappings ?? throw new ArgumentNullException(nameof(ioMappings)))
            .Where(static mapping => !string.IsNullOrWhiteSpace(mapping.PlcAddress))
            .Select(static mapping => new PlcIoScanMapping(
                mapping.SignalKey,
                mapping.PlcAddress,
                mapping.AddressCount,
                mapping.DataType,
                mapping.Direction,
                mapping.Category,
                mapping.SortOrder))
            .Where(static mapping => mapping.IsRead && IoMappingOptionCatalog.IsReadDataCategory(mapping.Category))
            .OrderBy(static mapping => mapping.SortOrder)
            .ToArray();

        _readBlocks = signalBlockPlanner.Plan(
            readMappings,
            policy.NormalizeMaxBlockWordCount(),
            policy.WriteGapPolicy,
            isWrite: false);
    }

    public string TaskName => $"PlcDataReadScan_{_device.DeviceName}";

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
        if (_readBlocks.Count == 0
            || !_plcService.IsConnected
            || (_statusStore is not null && !_statusStore.IsStableOnline(_device.Id)))
        {
            return;
        }

        var buffer = _dataStore.GetBuffer(_device.Id);
        if (buffer is null)
        {
            return;
        }

        await ReadPlcToBufferAsync(buffer).ConfigureAwait(false);
    }

    private async Task TaskCoreAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ExecuteOneCycleAsync(ct).ConfigureAwait(false);
                await Task.Delay(await ResolveDataReadLoopIntervalAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
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
                    _logger.Error($"[{_device.DeviceName}] PLC 只读数据扫描异常：{ex.Message}");
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

            if (_retryCount > 0)
            {
                _retryCount = 0;
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogDisconnect())
            {
                _logger.Error($"[{_device.DeviceName}] PLC 只读数据读取失败：{ex.Message}");
            }

            _plcService.Disconnect();
            _statusStore?.MarkDisconnected(_device.Id, _device.DeviceName, $"PLC 只读数据读取失败：{ex.Message}");
            throw new InvalidOperationException("PLC 只读数据读取链路失败，连接已重置。", ex);
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

    private async Task<int> ResolveDataReadLoopIntervalAsync(CancellationToken cancellationToken)
    {
        var configured = await _dataReadLoopIntervalResolver(cancellationToken).ConfigureAwait(false);
        return configured <= 0 ? PlcIoRuntimePolicy.Default.NormalizeDataReadLoopInterval() : configured;
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
