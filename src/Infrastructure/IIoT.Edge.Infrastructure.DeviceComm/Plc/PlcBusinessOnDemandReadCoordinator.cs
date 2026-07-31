using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Sdk.Hardware;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

/// <summary>
/// 为包含业务独占读信号的 TaskKey 集合触发一次真实 PLC 原子读取。每个规范化信号集合
/// 独立保存 in-flight 与完成快照，因此 MG1/MG2 不会互相消费或拼接周期缓存。
/// </summary>
internal sealed class PlcBusinessOnDemandReadCoordinator : IPlcOnDemandReadCoordinator
{
    private readonly object _sync = new();
    private readonly IPlcService _plcService;
    private readonly PlcBuffer _buffer;
    private readonly ILogService _logger;
    private readonly PlcConnectionStatusStore _statusStore;
    private readonly Action<bool> _connectionStateChanged;
    private readonly IPlcSignalBlockPlanner _blockPlanner;
    private readonly IReadOnlyDictionary<string, PlcIoScanMapping> _readMappings;
    private readonly IReadOnlySet<string> _businessOnDemandReadSignalKeys;
    private readonly PlcIoRuntimePolicy _runtimePolicy;
    private readonly Func<CancellationToken, Task<int>> _scanIntervalResolver;
    private readonly CancellationToken _runtimeCancellation;
    private readonly int _deviceId;
    private readonly string _plcCode;
    private readonly string _deviceName;
    private readonly Dictionary<string, RequestState> _requests =
        new(StringComparer.OrdinalIgnoreCase);
    private long _generation;

    public PlcBusinessOnDemandReadCoordinator(
        IPlcService plcService,
        PlcBuffer buffer,
        IReadOnlyCollection<PlcIoScanMapping> mappings,
        IReadOnlySet<string> businessOnDemandReadSignalKeys,
        ILogService logger,
        PlcConnectionStatusStore statusStore,
        Action<bool> connectionStateChanged,
        IPlcSignalBlockPlanner blockPlanner,
        PlcIoRuntimePolicy runtimePolicy,
        Func<CancellationToken, Task<int>> scanIntervalResolver,
        CancellationToken runtimeCancellation,
        int deviceId,
        string plcCode,
        string deviceName)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        ArgumentNullException.ThrowIfNull(mappings);
        _businessOnDemandReadSignalKeys = businessOnDemandReadSignalKeys
            ?? throw new ArgumentNullException(nameof(businessOnDemandReadSignalKeys));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        _connectionStateChanged = connectionStateChanged
            ?? throw new ArgumentNullException(nameof(connectionStateChanged));
        _blockPlanner = blockPlanner ?? throw new ArgumentNullException(nameof(blockPlanner));
        _runtimePolicy = runtimePolicy ?? throw new ArgumentNullException(nameof(runtimePolicy));
        _scanIntervalResolver = scanIntervalResolver
            ?? throw new ArgumentNullException(nameof(scanIntervalResolver));
        _runtimeCancellation = runtimeCancellation;
        _deviceId = deviceId;
        ArgumentException.ThrowIfNullOrWhiteSpace(plcCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        _plcCode = plcCode.Trim();
        _deviceName = deviceName.Trim();

        _readMappings = mappings
            .Where(static mapping => mapping.IsRead)
            .GroupBy(static mapping => mapping.SignalKey, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.Single(),
                StringComparer.OrdinalIgnoreCase);
    }

    public bool Handles(IReadOnlyCollection<string> requiredSignalKeys)
        => TryNormalizeRequest(requiredSignalKeys, out _, out _);

    public bool TryCapture(
        IReadOnlyCollection<string> requiredSignalKeys,
        out PlcReadBatchSnapshot? snapshot)
    {
        snapshot = null;
        if (!TryNormalizeRequest(requiredSignalKeys, out var requestKey, out var mappings))
        {
            return false;
        }

        RequestState state;
        var startRead = false;
        lock (_sync)
        {
            if (!_requests.TryGetValue(requestKey, out state!))
            {
                state = new RequestState(mappings);
                _requests.Add(requestKey, state);
            }

            if (state.CompletedSnapshot is not null)
            {
                snapshot = state.CompletedSnapshot;
                state.CompletedSnapshot = null;
            }

            if (!state.IsReading && !_runtimeCancellation.IsCancellationRequested)
            {
                state.IsReading = true;
                startRead = true;
            }
        }

        if (startRead)
        {
            _ = CompleteReadAsync(state);
        }

        return snapshot is not null;
    }

    private async Task CompleteReadAsync(RequestState state)
    {
        PlcReadBatchSnapshot? completed = null;
        try
        {
            completed = await ReadSnapshotAsync(state.Mappings, _runtimeCancellation)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_runtimeCancellation.IsCancellationRequested)
        {
        }
        catch (PlcServiceQuarantinedException ex)
        {
            _statusStore.MarkRuntimeFault(
                _deviceId,
                _plcCode,
                _deviceName,
                PlcServiceQuarantinedException.StableReasonCode);
            _connectionStateChanged(false);
            _logger.Error(
                $"[PlcCode={_plcCode}][按需读取] PLC service 已隔离，"
                + $"原因码={PlcServiceQuarantinedException.StableReasonCode}，异常类型={ex.GetType().Name}。");
        }
        catch (Exception ex)
        {
            var failure = PlcOperationFailureClassifier.Classify(ex);
            _logger.Error(
                $"[PlcCode={_plcCode}][按需读取] 原子读取协调器异常，{failure.SafeDiagnostic}。");
            try
            {
                completed = PublishFailureSnapshot(state.Mappings, failure.ReasonCode);
            }
            catch (Exception publishException)
            {
                var publishFailure = PlcOperationFailureClassifier.Classify(publishException);
                _logger.Error(
                    $"[PlcCode={_plcCode}][按需读取] 失败质量整批发布失败，"
                    + $"{publishFailure.SafeDiagnostic}；本轮保持无可消费快照。");
            }
        }
        finally
        {
            lock (_sync)
            {
                state.IsReading = false;
                if (completed is not null)
                {
                    state.CompletedSnapshot = completed;
                }
            }
        }
    }

    private async Task<PlcReadBatchSnapshot> ReadSnapshotAsync(
        IReadOnlyList<PlcIoScanMapping> mappings,
        CancellationToken cancellationToken)
    {
        var scanInterval = await _scanIntervalResolver(cancellationToken).ConfigureAwait(false);
        using var scheduling = PlcOperationSchedulingContext.Push(
            PlcOperationPriority.BusinessOnDemand,
            TimeSpan.FromMilliseconds(scanInterval));

        var blocks = _blockPlanner.Plan(
            mappings,
            _runtimePolicy.NormalizeMaxBlockWordCount(),
            _runtimePolicy.WriteGapPolicy,
            isWrite: false);
        var batchId = Guid.NewGuid();
        var attemptedAtUtc = DateTimeOffset.UtcNow;
        var updates = new Dictionary<string, PlcReadSignalUpdate>(StringComparer.OrdinalIgnoreCase);
        string? remainingFailureReason = null;

        foreach (var block in blocks)
        {
            if (remainingFailureReason is not null || !_plcService.IsConnected)
            {
                StageFailedBlock(
                    updates,
                    block,
                    batchId,
                    attemptedAtUtc,
                    remainingFailureReason ?? PlcTaskRuntimeErrorCodes.TransportDisconnected);
                remainingFailureReason ??= PlcTaskRuntimeErrorCodes.TransportDisconnected;
                continue;
            }

            try
            {
                var data = await _plcService
                    .ReadDataAsync<ushort>(
                        block.StartAddress,
                        checked((ushort)block.WordCount),
                        cancellationToken)
                    .ConfigureAwait(false);
                var words = data.ToArray();
                if (words.Length < block.WordCount)
                {
                    throw new InvalidDataException(
                        $"PLC 返回字数不足：期望 {block.WordCount}，实际 {words.Length}。");
                }

                foreach (var item in block.Items)
                {
                    StageSuccessfulItem(
                        updates,
                        item,
                        words,
                        batchId,
                        attemptedAtUtc);
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
            catch (Exception ex) when (
                PlcOperationFailureClassifier.IsCallerCancellation(ex, cancellationToken))
            {
                throw;
            }
            catch (Exception ex)
            {
                var failure = PlcOperationFailureClassifier.Classify(ex);
                StageFailedBlock(updates, block, batchId, attemptedAtUtc, failure.ReasonCode);
                _logger.Error(
                    $"[PlcCode={_plcCode}][按需读取] PLC block 失败，地址={block.StartAddress}，"
                    + $"长度={block.WordCount}，{failure.SafeDiagnostic}；整批质量失败关闭。");
                if (failure.DisconnectsTransport)
                {
                    remainingFailureReason = PlcTaskRuntimeErrorCodes.TransportDisconnected;
                    await HandleTransportFailureAsync(block, failure).ConfigureAwait(false);
                }
            }
        }

        var snapshot = PublishSnapshot(updates);
        if (updates.Values.Any(static update => update.ReadSucceeded))
        {
            _statusStore.MarkProtocolSuccess(
                _deviceId,
                _plcCode,
                _deviceName);
        }

        return snapshot;
    }

    private PlcReadBatchSnapshot PublishFailureSnapshot(
        IReadOnlyList<PlcIoScanMapping> mappings,
        string failureReason)
    {
        var batchId = Guid.NewGuid();
        var attemptedAtUtc = DateTimeOffset.UtcNow;
        var updates = mappings.ToDictionary(
            static mapping => mapping.SignalKey,
            mapping => new PlcReadSignalUpdate(
                new ushort[Math.Max(1, mapping.AddressCount)],
                ReadSucceeded: false,
                batchId,
                attemptedAtUtc,
                failureReason),
            StringComparer.OrdinalIgnoreCase);
        return PublishSnapshot(updates);
    }

    private PlcReadBatchSnapshot PublishSnapshot(
        IReadOnlyDictionary<string, PlcReadSignalUpdate> updates)
    {
        ((IPlcReadBatchPublisher)_buffer).PublishReadBatch(updates);
        var generation = Interlocked.Increment(ref _generation);
        var first = updates.Values.First();
        var signals = updates.Select(pair => new PlcReadSignalSnapshot(
            pair.Key,
            generation,
            pair.Value.BatchId,
            pair.Value.AttemptedAtUtc,
            pair.Value.CurrentWords,
            pair.Value.ReadSucceeded,
            pair.Value.FailureReason)).ToArray();
        return new PlcReadBatchSnapshot(
            generation,
            first.BatchId,
            first.AttemptedAtUtc,
            signals);
    }

    private async Task HandleTransportFailureAsync(
        PlcSignalBlock block,
        PlcOperationFailure failure)
    {
        _statusStore.MarkDisconnected(
            _deviceId,
            _plcCode,
            _deviceName,
            $"{failure.ReasonCode}: 操作=BusinessRead，地址={block.StartAddress}，长度={block.WordCount}。");
        _connectionStateChanged(false);
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
        catch (Exception ex)
        {
            var disconnectFailure = PlcOperationFailureClassifier.Classify(ex);
            _logger.Error(
                $"[PlcCode={_plcCode}][按需读取] transport 故障后的连接释放失败，"
                + $"{disconnectFailure.SafeDiagnostic}。");
        }
    }

    private bool TryNormalizeRequest(
        IReadOnlyCollection<string> requiredSignalKeys,
        out string requestKey,
        out IReadOnlyList<PlcIoScanMapping> mappings)
    {
        requestKey = string.Empty;
        mappings = [];
        if (requiredSignalKeys is null || requiredSignalKeys.Count == 0)
        {
            return false;
        }

        var normalizedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signalKey in requiredSignalKeys)
        {
            if (string.IsNullOrWhiteSpace(signalKey)
                || !normalizedKeys.Add(signalKey.Trim()))
            {
                return false;
            }
        }

        if (!normalizedKeys.Any(_businessOnDemandReadSignalKeys.Contains))
        {
            return false;
        }

        var resolved = new List<PlcIoScanMapping>(normalizedKeys.Count);
        foreach (var signalKey in normalizedKeys.OrderBy(
                     static key => key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!_readMappings.TryGetValue(signalKey, out var mapping))
            {
                return false;
            }

            resolved.Add(mapping);
        }

        requestKey = string.Join('\u001f', resolved.Select(static mapping => mapping.SignalKey));
        mappings = resolved;
        return true;
    }

    private static void StageFailedBlock(
        IDictionary<string, PlcReadSignalUpdate> updates,
        PlcSignalBlock block,
        Guid batchId,
        DateTimeOffset attemptedAtUtc,
        string failureReason)
    {
        foreach (var item in block.Items)
        {
            updates[item.Mapping.SignalKey] = new PlcReadSignalUpdate(
                new ushort[Math.Max(1, item.Mapping.AddressCount)],
                ReadSucceeded: false,
                batchId,
                attemptedAtUtc,
                failureReason);
        }
    }

    private static void StageSuccessfulItem(
        IDictionary<string, PlcReadSignalUpdate> updates,
        PlcSignalBlockItem item,
        IReadOnlyList<ushort> blockWords,
        Guid batchId,
        DateTimeOffset attemptedAtUtc)
    {
        if (updates.TryGetValue(item.Mapping.SignalKey, out var existing)
            && !existing.ReadSucceeded)
        {
            return;
        }

        var signalWords = existing is null
            ? new ushort[Math.Max(1, item.Mapping.AddressCount)]
            : (ushort[])existing.CurrentWords.Clone();
        for (var index = 0; index < item.EffectiveWordCount; index++)
        {
            var sourceIndex = item.Offset + index;
            var targetIndex = item.MappingWordOffset + index;
            if (sourceIndex >= 0
                && sourceIndex < blockWords.Count
                && targetIndex >= 0
                && targetIndex < signalWords.Length)
            {
                signalWords[targetIndex] = blockWords[sourceIndex];
            }
        }

        updates[item.Mapping.SignalKey] = new PlcReadSignalUpdate(
            signalWords,
            ReadSucceeded: true,
            batchId,
            attemptedAtUtc,
            FailureReason: null);
    }

    private sealed class RequestState(IReadOnlyList<PlcIoScanMapping> mappings)
    {
        public IReadOnlyList<PlcIoScanMapping> Mappings { get; } = mappings;

        public bool IsReading { get; set; }

        public PlcReadBatchSnapshot? CompletedSnapshot { get; set; }
    }
}
