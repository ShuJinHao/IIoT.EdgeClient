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
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

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
    private readonly Action<bool>? _connectionStateChanged;
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
        Func<CancellationToken, Task<int>>? dataReadLoopIntervalResolver = null,
        Action<bool>? connectionStateChanged = null)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _device = deviceConfig ?? throw new ArgumentNullException(nameof(deviceConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _statusStore = statusStore;
        _connectionStateChanged = connectionStateChanged;
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
            || !_plcService.IsConnected)
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

        if (hasSuccessfulRead)
        {
            _statusStore?.MarkProtocolSuccess(
                _device.Id,
                _device.PlcCode,
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
                _statusStore?.MarkRuntimeFault(
                    _device.Id,
                    _device.PlcCode,
                    _device.DeviceName,
                    ex.Message);
                _connectionStateChanged?.Invoke(false);
                _logger.Error($"[PlcCode={_device.PlcCode}][采集] PLC service 已隔离，只读任务已停止：{ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                _retryCount++;
                if (ShouldLogDisconnect())
                {
                    _logger.Error($"[PlcCode={_device.PlcCode}] PLC 只读数据扫描异常：{ex.Message}");
                }

                await Task.Delay(GetBackoffDelay(), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> ReadPlcToBufferAsync(
        IPlcBufferTransport buffer,
        CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid();
        var attemptedAtUtc = DateTimeOffset.UtcNow;
        var stagedUpdates = new Dictionary<string, PlcReadSignalUpdate>(StringComparer.OrdinalIgnoreCase);
        var hasSuccessfulRead = false;
        string? remainingFailureReason = null;

        foreach (var block in _readBlocks)
        {
            if (remainingFailureReason is not null || !_plcService.IsConnected)
            {
                StageFailedBlock(
                    stagedUpdates,
                    block,
                    batchId,
                    attemptedAtUtc,
                    remainingFailureReason ?? "PLC transport 当前不可用，未发起本 block 读取。");
                continue;
            }

            try
            {
                var data = await _plcService
                    .ReadDataAsync<ushort>(block.StartAddress, (ushort)block.WordCount, cancellationToken)
                    .ConfigureAwait(false);
                var words = data.ToArray();
                if (words.Length < block.WordCount)
                {
                    throw new InvalidDataException(
                        $"PLC 返回字数不足：期望 {block.WordCount}，实际 {words.Length}。");
                }

                foreach (var item in block.Items)
                {
                    stagedUpdates[item.Mapping.SignalKey] = new PlcReadSignalUpdate(
                        SliceWords(words, item.Offset, item.Mapping.AddressCount),
                        ReadSucceeded: true,
                        batchId,
                        attemptedAtUtc,
                        FailureReason: null);
                }

                hasSuccessfulRead = true;
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
                var failureReason = FormatException(ex);
                StageFailedBlock(
                    stagedUpdates,
                    block,
                    batchId,
                    attemptedAtUtc,
                    failureReason);

                var isTransportFailure = PlcOperationFailureClassifier.IsTransportFailure(ex);
                LogBlockFailure(block, ex, isTransportFailure);
                if (isTransportFailure)
                {
                    remainingFailureReason =
                        $"前序 block 已发生明确 transport 故障：{failureReason}";
                    await HandleTransportFailureAsync(block, ex).ConfigureAwait(false);
                }
                else if (!_plcService.IsConnected)
                {
                    remainingFailureReason =
                        $"前序 block 失败后 PLC service 暂不可用：{failureReason}";
                }
            }
        }

        CommitReadBatch(buffer, stagedUpdates);
        if (hasSuccessfulRead)
        {
            _retryCount = 0;
        }

        return hasSuccessfulRead;
    }

    private void LogBlockFailure(
        PlcSignalBlock block,
        Exception exception,
        bool isTransportFailure)
    {
        var message =
            $"PLC 只读 block 失败，地址={block.StartAddress}，长度={block.WordCount}，信号={FormatBlockSignals(block)}，失败类型={(isTransportFailure ? "Transport" : "Request")}，原因={FormatException(exception)}；受影响信号已发布默认值与失败质量，未执行单信号重读。";

        if (ShouldLogDisconnect())
        {
            _logger.Error($"[PlcCode={_device.PlcCode}][采集] {message}");
        }
    }

    private async Task HandleTransportFailureAsync(
        PlcSignalBlock block,
        Exception exception)
    {
        var message =
            $"PLC transport 故障，操作=Read，地址={block.StartAddress}，长度={block.WordCount}，原因={FormatException(exception)}。";
        _statusStore?.MarkDisconnected(
            _device.Id,
            _device.PlcCode,
            _device.DeviceName,
            message);
        _connectionStateChanged?.Invoke(false);

        if (!_plcService.IsConnected)
        {
            return;
        }

        try
        {
            await _plcService.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (PlcServiceQuarantinedException)
        {
            throw;
        }
        catch (Exception disconnectException)
        {
            _logger.Error(
                $"[PlcCode={_device.PlcCode}][采集] transport 故障后的连接释放失败：{FormatException(disconnectException)}");
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

    private static void StageFailedBlock(
        IDictionary<string, PlcReadSignalUpdate> stagedUpdates,
        PlcSignalBlock block,
        Guid batchId,
        DateTimeOffset attemptedAtUtc,
        string failureReason)
    {
        foreach (var item in block.Items)
        {
            stagedUpdates[item.Mapping.SignalKey] = new PlcReadSignalUpdate(
                new ushort[Math.Max(1, item.Mapping.AddressCount)],
                ReadSucceeded: false,
                batchId,
                attemptedAtUtc,
                failureReason);
        }
    }

    private static void CommitReadBatch(
        IPlcBufferTransport buffer,
        IReadOnlyDictionary<string, PlcReadSignalUpdate> stagedUpdates)
    {
        if (buffer is not IPlcReadBatchPublisher publisher)
        {
            throw new InvalidOperationException(
                "PLC buffer 不具备强制整批发布能力，拒绝逐信号降级提交读取结果。");
        }

        publisher.PublishReadBatch(stagedUpdates);
    }

    private static string FormatBlockSignals(PlcSignalBlock block)
        => string.Join(
            "、",
            block.Items.Select(static item =>
                $"{item.Mapping.SignalKey}@{item.Mapping.PlcAddress}[{Math.Max(1, item.Mapping.AddressCount)}]"));

    private static string FormatException(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null && messages.Count < 4; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message)
                && !messages.Contains(current.Message, StringComparer.Ordinal))
            {
                messages.Add(current.Message);
            }
        }

        return messages.Count == 0
            ? exception.GetType().Name
            : string.Join(" -> ", messages);
    }

    private static int ToLatencyMs(long elapsedMilliseconds)
        => (int)Math.Min(int.MaxValue, Math.Max(0, elapsedMilliseconds));
}
