using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
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
        var connectStopwatch = Stopwatch.StartNew();
        var preserveConnectedProjection = IsStableOnline();
        try
        {
            _plcService.Init(endpoint);
            if (!preserveConnectedProjection)
            {
                MarkConnecting();
            }

            var connected = await _plcService.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            if (connected)
            {
                MarkConnected(ToLatencyMs(connectStopwatch.ElapsedMilliseconds));
                LogRecoveredIfNeeded(isStableOnline: true);
                return;
            }

            _retryCount++;
            MarkDisconnected("PLC 连接失败。");
            if (ShouldLogDisconnect())
            {
                _logger.Warn($"[PlcCode={_device.PlcCode}] PLC 连接失败，进入退避重试。");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlcServiceQuarantinedException ex)
        {
            MarkRuntimeFault(PlcServiceQuarantinedException.StableReasonCode);
            _logger.Error(
                $"[PlcCode={_device.PlcCode}] PLC service 已隔离，"
                + $"原因码={PlcServiceQuarantinedException.StableReasonCode}，异常类型={ex.GetType().Name}。");
            throw;
        }
        catch (Exception ex) when (
            PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = PlcOperationFailureClassifier.Classify(ex);
            if (failure.DisconnectsTransport)
            {
                await CloseHangingConnectionAsync(CancellationToken.None).ConfigureAwait(false);
                MarkDisconnected(failure.ReasonCode);
            }

            _retryCount++;
            if (ShouldLogDisconnect())
            {
                _logger.Error(
                    $"[PlcCode={_device.PlcCode}] PLC 连接异常，{failure.SafeDiagnostic}；"
                    + "连接状态按分类保持，稍后重试。");
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

        if (_readBlocks.Count > 0)
        {
            var cycleStopwatch = Stopwatch.StartNew();
            var readResult = await ReadPlcToBufferAsync(buffer, ct).ConfigureAwait(false);
            if (readResult.AnySucceeded)
            {
                MarkProtocolSuccess(ToLatencyMs(cycleStopwatch.ElapsedMilliseconds));
            }

            if (!readResult.AllSucceeded)
            {
                return;
            }
        }

        if (_writeBlocks.Count > 0)
        {
            await WriteBufferToPlcAsync(buffer, ct).ConfigureAwait(false);
        }
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
                MarkRuntimeFault(PlcServiceQuarantinedException.StableReasonCode);
                _logger.Error(
                    $"[PlcCode={_device.PlcCode}] PLC service 已隔离，扫描任务已停止，"
                    + $"原因码={PlcServiceQuarantinedException.StableReasonCode}，异常类型={ex.GetType().Name}。");
                throw;
            }
            catch (Exception ex) when (
                PlcOperationFailureClassifier.IsCallerCancellation(ex, ct))
            {
                throw;
            }
            catch (Exception ex)
            {
                var failure = PlcOperationFailureClassifier.Classify(ex);
                _retryCount++;
                if (ShouldLogDisconnect())
                {
                    _logger.Error(
                        $"[PlcCode={_device.PlcCode}] PLC 信号交互循环异常，{failure.SafeDiagnostic}。");
                }

                await Task.Delay(GetBackoffDelay(), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<PlcReadCycleResult> ReadPlcToBufferAsync(
        IPlcBufferTransport buffer,
        CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid();
        var attemptedAtUtc = DateTimeOffset.UtcNow;
        var stagedUpdates = new Dictionary<string, PlcReadSignalUpdate>(StringComparer.OrdinalIgnoreCase);
        var allSucceeded = true;
        var anySucceeded = false;
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
                    remainingFailureReason ?? PlcTaskRuntimeErrorCodes.TransportDisconnected);
                allSucceeded = false;
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

                anySucceeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PlcServiceQuarantinedException)
            {
                throw;
            }
            catch (Exception ex) when (
                PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
            {
                throw;
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                var failure = PlcOperationFailureClassifier.Classify(ex);
                var failureReason = failure.ReasonCode;
                StageFailedBlock(
                    stagedUpdates,
                    block,
                    batchId,
                    attemptedAtUtc,
                    failureReason);

                LogReadFailure(block, failure);
                if (failure.DisconnectsTransport)
                {
                    remainingFailureReason =
                        PlcTaskRuntimeErrorCodes.TransportDisconnected;
                    await HandleTransportFailureAsync(
                            "Read",
                            block.StartAddress,
                            block.WordCount,
                            failure)
                        .ConfigureAwait(false);
                }
                else if (!_plcService.IsConnected)
                {
                    remainingFailureReason = failure.ReasonCode;
                }
            }
        }

        CommitReadBatch(buffer, stagedUpdates);
        return new PlcReadCycleResult(allSucceeded, anySucceeded);
    }

    private void LogRecoveredIfNeeded(bool isStableOnline)
    {
        if (!isStableOnline || (_retryCount <= 0 && _lastDisconnectLogTime == DateTime.MinValue))
        {
            return;
        }

        _logger.Info($"[PlcCode={_device.PlcCode}] PLC 连接已恢复。");
        _lastDisconnectLogTime = DateTime.MinValue;
        _retryCount = 0;
    }

    private async Task<bool> WriteBufferToPlcAsync(
        IPlcBufferTransport buffer,
        CancellationToken cancellationToken)
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

            try
            {
                await _plcService
                    .WriteDataAsync(block.StartAddress, blockWords.ToList(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PlcServiceQuarantinedException)
            {
                throw;
            }
            catch (Exception ex) when (
                PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
            {
                throw;
            }
            catch (Exception ex)
            {
                var failure = PlcOperationFailureClassifier.Classify(ex);
                if (ShouldLogDisconnect())
                {
                    _logger.Error(
                        $"[PlcCode={_device.PlcCode}][交互] PLC 写入失败，地址={block.StartAddress}，"
                        + $"长度={block.WordCount}，{failure.SafeDiagnostic}；待写 buffer 保持未消费。");
                }

                if (failure.DisconnectsTransport)
                {
                    await HandleTransportFailureAsync(
                            "Write",
                            block.StartAddress,
                            block.WordCount,
                            failure)
                        .ConfigureAwait(false);
                }

                return false;
            }
        }

        return true;
    }

    private void LogReadFailure(
        PlcSignalBlock block,
        PlcOperationFailure failure)
    {
        if (!ShouldLogDisconnect())
        {
            return;
        }

        _logger.Error(
            $"[PlcCode={_device.PlcCode}][交互] PLC 读取 block 失败，地址={block.StartAddress}，"
            + $"长度={block.WordCount}，{failure.SafeDiagnostic}；"
            + "受影响信号已发布默认值与失败质量，未执行单信号重读。");
    }

    private async Task HandleTransportFailureAsync(
        string operation,
        string address,
        int wordCount,
        PlcOperationFailure failure)
    {
        var reason =
            $"{failure.ReasonCode}: 操作={operation}，地址={address}，长度={wordCount}。";
        MarkDisconnected(reason);

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
            var disconnectFailure = PlcOperationFailureClassifier.Classify(disconnectException);
            _logger.Error(
                $"[PlcCode={_device.PlcCode}][交互] transport 故障后的连接释放失败，"
                + $"{disconnectFailure.SafeDiagnostic}。");
        }
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

    private readonly record struct PlcReadCycleResult(bool AllSucceeded, bool AnySucceeded);
}

/// <summary>
/// PLC IO 扫描任务绑定的设备信息。
/// </summary>
public sealed record PlcIoScanDevice(int DeviceId, string DeviceName, PlcEndpoint Endpoint)
{
    public string PlcCode { get; init; } = string.Empty;
}

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
