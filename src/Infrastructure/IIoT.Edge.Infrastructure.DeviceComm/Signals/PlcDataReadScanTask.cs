using System.Diagnostics;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Signals;

/// <summary>
/// PLC 只读数据扫描任务，负责把单点读数据和连续读数据刷新到运行缓冲区。
/// </summary>
public sealed class PlcDataReadScanTask : IPlcTask
{
    private const int DisconnectLogIntervalSeconds = 30;
    private const int ConnectionFailureThreshold = 3;

    private readonly IPlcService _plcService;
    private readonly IPlcDataStore _dataStore;
    private readonly NetworkDeviceEntity _device;
    private readonly ILogService _logger;
    private readonly PlcConnectionStatusStore? _statusStore;
    private readonly Func<CancellationToken, Task<int>> _dataReadLoopIntervalResolver;
    private readonly IReadOnlyList<PlcSignalBlock> _readBlocks;
    private readonly bool _canPromoteConnectionFromReadData;
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

        var scanMappings = (ioMappings ?? throw new ArgumentNullException(nameof(ioMappings)))
            .Where(static mapping => !string.IsNullOrWhiteSpace(mapping.PlcAddress))
            .Select(static mapping => new PlcIoScanMapping(
                mapping.SignalKey,
                mapping.PlcAddress,
                mapping.AddressCount,
                mapping.DataType,
                mapping.Direction,
                mapping.Category,
                mapping.SortOrder))
            .ToArray();

        var hasInteractionMapping = scanMappings.Any(static mapping =>
            IoMappingOptionCatalog.IsInteractionCategory(mapping.Category));
        var hasWriteMapping = scanMappings.Any(static mapping => mapping.IsWrite);
        _canPromoteConnectionFromReadData = !hasInteractionMapping && !hasWriteMapping;

        var readMappings = scanMappings
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

    public Task StartAsync(CancellationToken ct)
        => TaskCoreAsync(ct);

    public async Task ExecuteOneCycleAsync(CancellationToken ct)
    {
        if (_readBlocks.Count == 0
            || !_plcService.IsConnected
            || (_statusStore is not null
                && !_canPromoteConnectionFromReadData
                && !_statusStore.IsStableOnline(_device.Id)))
        {
            return;
        }

        var buffer = _dataStore.GetBuffer(_device.Id);
        if (buffer is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var hasSuccessfulRead = await ReadPlcToBufferAsync(buffer, ct).ConfigureAwait(false);
        stopwatch.Stop();

        if (hasSuccessfulRead && _canPromoteConnectionFromReadData)
        {
            _statusStore?.MarkProtocolSuccess(
                _device.Id,
                _device.DeviceName,
                ToLatencyMs(stopwatch.ElapsedMilliseconds));
        }
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (PlcServiceQuarantinedException ex)
            {
                _statusStore?.MarkRuntimeFault(_device.Id, _device.DeviceName, ex.Message);
                _logger.Error($"[PLC-{_device.DeviceName}][采集] PLC service 已隔离，只读任务已停止：{ex.Message}");
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

    private async Task<bool> ReadPlcToBufferAsync(
        IPlcBufferTransport buffer,
        CancellationToken cancellationToken)
    {
        var hasSuccessfulRead = false;
        var hasFailedRead = false;

        foreach (var block in _readBlocks)
        {
            var blockSucceeded = await TryReadBlockToBufferAsync(buffer, block, cancellationToken).ConfigureAwait(false);
            hasSuccessfulRead |= blockSucceeded;
            hasFailedRead |= !blockSucceeded;
        }

        if (hasSuccessfulRead)
        {
            _retryCount = 0;
            return true;
        }

        if (!hasFailedRead)
        {
            return false;
        }

        _retryCount++;
        if (_retryCount < ConnectionFailureThreshold)
        {
            return false;
        }

        var message = $"PLC 只读数据连续 {_retryCount} 轮未读取到任何成功 block。";
        if (ShouldLogDisconnect())
        {
            _logger.Error($"[PLC-{_device.DeviceName}][采集] {message}");
        }

        await _plcService.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _statusStore?.MarkDisconnected(_device.Id, _device.DeviceName, message);
        throw new InvalidOperationException($"PLC 只读数据读取链路失败，连接已重置：{message}");
    }

    private async Task<bool> TryReadBlockToBufferAsync(
        IPlcBufferTransport buffer,
        PlcSignalBlock block,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await _plcService
                .ReadDataAsync<ushort>(block.StartAddress, (ushort)block.WordCount, cancellationToken)
                .ConfigureAwait(false);
            var words = data.ToArray();

            foreach (var item in block.Items)
            {
                buffer.UpdateReadSignal(
                    item.Mapping.SignalKey,
                    SliceWords(words, item.Offset, item.Mapping.AddressCount));
            }

            return true;
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
                _logger.Error(
                    $"[PLC-{_device.DeviceName}][采集] 只读 block 读取失败，地址={block.StartAddress}，长度={block.WordCount}，信号={FormatBlockSignals(block)}，原因={ex.Message}");
            }

            var hasSignalSuccess = false;
            foreach (var item in block.Items)
            {
                try
                {
                    var data = await _plcService
                        .ReadDataAsync<ushort>(
                            item.Mapping.PlcAddress,
                            (ushort)item.Mapping.AddressCount,
                            cancellationToken)
                        .ConfigureAwait(false);
                    buffer.UpdateReadSignal(
                        item.Mapping.SignalKey,
                        SliceWords(data, 0, item.Mapping.AddressCount));
                    hasSignalSuccess = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (PlcServiceQuarantinedException)
                {
                    throw;
                }
                catch
                {
                    ClearReadSignal(buffer, item);
                }
            }

            return hasSignalSuccess;
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

    private static void ClearReadSignal(IPlcBufferTransport buffer, PlcSignalBlockItem item)
        => buffer.UpdateReadSignal(
            item.Mapping.SignalKey,
            new ushort[Math.Max(1, item.Mapping.AddressCount)]);

    private static string FormatBlockSignals(PlcSignalBlock block)
        => string.Join(
            "、",
            block.Items.Select(static item =>
                $"{item.Mapping.SignalKey}@{item.Mapping.PlcAddress}[{Math.Max(1, item.Mapping.AddressCount)}]"));

    private static int ToLatencyMs(long elapsedMilliseconds)
        => (int)Math.Min(int.MaxValue, Math.Max(0, elapsedMilliseconds));
}
