using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.DeviceLog;
using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Abstractions.Shared;

namespace IIoT.Edge.Testing;

public sealed class FakeProductionTimeProvider : IProductionTimeProvider
{
    public TimeZoneInfo BusinessTimeZone { get; } = ResolveChinaTimeZone();

    public DateTime? FixedUtcNow { get; set; }

    public DateTime UtcNow => FixedUtcNow ?? DateTime.UtcNow;

    public DateTime BusinessNow => ToBusinessTime(UtcNow);

    public DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        var businessTime = value.Kind == DateTimeKind.Local
            ? TimeZoneInfo.ConvertTime(value, BusinessTimeZone)
            : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(businessTime, BusinessTimeZone);
    }

    public DateTime ToBusinessTime(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : ToUtc(value);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, BusinessTimeZone), DateTimeKind.Unspecified);
    }

    public string FormatBusinessTimestamp(DateTime value)
        => ToBusinessTime(value).ToString("yyyy-MM-dd HH:mm:ss");

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        foreach (var id in new[] { "China Standard Time", "Asia/Shanghai" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }
}

public sealed class FakeLogService : ILogService
{
    public List<LogEntry> Entries { get; } = new();

    public event Action<LogEntry>? EntryAdded;

    public void Debug(string message) => Write("Debug", message);
    public void Info(string message) => Write("Info", message);
    public void Warn(string message) => Write("Warn", message);
    public void Error(string message) => Write("Error", message);
    public void Fatal(string message) => Write("Fatal", message);

    private void Write(string level, string message)
    {
        var entry = new LogEntry
        {
            Time = DateTime.UtcNow,
            Level = level,
            Message = message
        };

        Entries.Add(entry);
        EntryAdded?.Invoke(entry);
    }
}

public sealed class FakeDeviceService : IDeviceService, IDeviceAccessTokenProvider
{
    public DeviceSession? CurrentDevice { get; set; }
    public string? AccessToken => CurrentDevice?.UploadAccessToken;
    public DateTimeOffset? AccessTokenExpiresAtUtc => CurrentDevice?.UploadAccessTokenExpiresAtUtc;
    public NetworkState CurrentState { get; set; } = NetworkState.Offline;
    public EdgeUploadGateSnapshot CurrentUploadGate { get; set; } = new()
    {
        State = EdgeUploadGateState.Unknown,
        Reason = EdgeUploadBlockReason.DeviceUnidentified
    };
    public bool HasDeviceId { get; set; }
    public bool CanUploadToCloud { get; set; }
    public int RefreshBootstrapCallCount { get; private set; }
    public Func<CancellationToken, Task>? RefreshBootstrapHandler { get; set; }

    public event Action<NetworkState>? NetworkStateChanged;
    public event Action<DeviceSession?>? DeviceIdentified;
    public event Action<EdgeUploadGateSnapshot>? UploadGateChanged;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public async Task RefreshBootstrapAsync(CancellationToken ct = default)
    {
        RefreshBootstrapCallCount++;
        if (RefreshBootstrapHandler is not null)
        {
            await RefreshBootstrapHandler(ct);
        }
    }

    public void SetOnline(DeviceSession session)
    {
        CurrentDevice = session;
        CurrentState = NetworkState.Online;
        HasDeviceId = true;
        CanUploadToCloud = true;
        CurrentUploadGate = new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Ready,
            Reason = EdgeUploadBlockReason.None,
            TokenExpiresAtUtc = session.UploadAccessTokenExpiresAtUtc,
            LastBootstrapSucceededAtUtc = DateTimeOffset.UtcNow
        };
        DeviceIdentified?.Invoke(session);
        NetworkStateChanged?.Invoke(CurrentState);
        UploadGateChanged?.Invoke(CurrentUploadGate);
    }

    public void SetOffline()
    {
        CurrentState = NetworkState.Offline;
        HasDeviceId = false;
        CanUploadToCloud = false;
        CurrentUploadGate = new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = EdgeUploadBlockReason.BootstrapNetworkFailure,
            TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc,
            LastBootstrapFailedAtUtc = DateTimeOffset.UtcNow
        };
        NetworkStateChanged?.Invoke(CurrentState);
        UploadGateChanged?.Invoke(CurrentUploadGate);
    }

    public void SetUploadGate(EdgeUploadGateSnapshot snapshot)
    {
        CurrentUploadGate = snapshot;
        CanUploadToCloud = snapshot.State == EdgeUploadGateState.Ready;
        UploadGateChanged?.Invoke(snapshot);
    }

    public void MarkUploadGateBlocked(EdgeUploadBlockReason reason, DateTimeOffset occurredAtUtc)
    {
        CurrentUploadGate = new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = reason,
            TokenExpiresAtUtc = CurrentDevice?.UploadAccessTokenExpiresAtUtc,
            LastBootstrapFailedAtUtc = occurredAtUtc
        };
        CanUploadToCloud = false;
        UploadGateChanged?.Invoke(CurrentUploadGate);
    }
}

public sealed class FakeLocalSystemRuntimeConfigService : ILocalSystemRuntimeConfigService
{
    public SystemRuntimeConfigSnapshot Current { get; set; } = SystemRuntimeConfigSnapshot.Default with
    {
        SystemCloudEnabled = true
    };

    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public sealed class FakeDataPipelineService : IDataPipelineService
{
    private readonly Queue<CellCompletedRecord> _queue = new();

    public int PendingCount => _queue.Count;
    public int OverflowCount { get; private set; }
    public int SpillCount { get; private set; }

    public ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default)
    {
        _queue.Enqueue(record);
        return ValueTask.FromResult(DataPipelineEnqueueResult.Accepted());
    }

    public bool TryDequeue(out CellCompletedRecord? record)
    {
        if (_queue.Count == 0)
        {
            record = null;
            return false;
        }

        record = _queue.Dequeue();
        return true;
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_queue.Count > 0);
}

public sealed class FakeCellDataConsumer : ICellDataConsumer
{
    private readonly bool _result;
    private readonly Func<CellCompletedRecord, CancellationToken, Task<bool>>? _processAsync;

    public FakeCellDataConsumer(
        string name,
        int order,
        string? retryChannel,
        bool result,
        ConsumerFailureMode failureMode = ConsumerFailureMode.BestEffort,
        Func<CellCompletedRecord, CancellationToken, Task<bool>>? processAsync = null)
    {
        Name = name;
        Order = order;
        RetryChannel = ParseRetryChannel(retryChannel);
        _result = result;
        FailureMode = failureMode;
        _processAsync = processAsync;
    }

    public string Name { get; }
    public int Order { get; }
    public ConsumerFailureMode FailureMode { get; }
    public DataPipelineRetryChannel RetryChannel { get; }

    public int ProcessCallCount { get; private set; }

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessCallCount++;

        if (_processAsync is not null)
        {
            return await _processAsync(record, cancellationToken);
        }

        return _result;
    }

    private static DataPipelineRetryChannel ParseRetryChannel(string? retryChannel)
        => retryChannel?.ToUpperInvariant() switch
        {
            "CLOUD" => DataPipelineRetryChannel.Cloud,
            "MES" => DataPipelineRetryChannel.Mes,
            _ => DataPipelineRetryChannel.None
        };
}

public sealed class FakeExternalHeartbeatStateStore : IExternalHeartbeatStateStore
{
    private readonly Dictionary<ExternalSystemKind, ExternalHeartbeatSnapshot> _snapshots = new();

    public ExternalHeartbeatSnapshot Get(ExternalSystemKind system)
        => _snapshots.TryGetValue(system, out var snapshot)
            ? snapshot
            : ExternalHeartbeatSnapshot.Unknown(system);

    public void MarkReady(
        ExternalSystemKind system,
        DateTime? occurredAtUtc = null,
        string? message = null,
        int? latencyMs = null)
    {
        var occurredAt = occurredAtUtc ?? DateTime.UtcNow;
        _snapshots[system] = Get(system) with
        {
            State = ExternalHeartbeatState.Ready,
            ReasonCode = "ready",
            Message = message,
            LastAttemptAtUtc = occurredAt,
            LastSuccessAtUtc = occurredAt,
            LatencyMs = latencyMs
        };
    }

    public void MarkNotReady(
        ExternalSystemKind system,
        string reasonCode,
        string? message = null,
        DateTime? occurredAtUtc = null,
        int? latencyMs = null)
    {
        var occurredAt = occurredAtUtc ?? DateTime.UtcNow;
        _snapshots[system] = Get(system) with
        {
            State = ExternalHeartbeatState.NotReady,
            ReasonCode = reasonCode,
            Message = message,
            LastAttemptAtUtc = occurredAt,
            LastFailureAtUtc = occurredAt,
            LatencyMs = latencyMs
        };
    }
}

public sealed class FakeFailedRecordStore : ICloudRetryRecordStore, IMesRetryRecordStore
{
    private readonly Dictionary<string, List<long>> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _claimedRecordIds = new();

    public sealed record RetryUpdate(long Id, int RetryCount, string ErrorMessage, DateTime NextRetryTime);

    public List<FailedCellRecord> PendingRecords { get; } = new();
    public Dictionary<long, RetryUpdate> Updates { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public List<string> DeletedClaimTokens { get; } = new();
    public List<string> ReleasedClaimTokens { get; } = new();
    public int ResetAllAbandonedCallCount { get; private set; }
    public int DeleteExpiredAbandonedCallCount { get; private set; }
    public int SaveCallCount { get; private set; }
    public int ClaimCallCount { get; private set; }
    public int ReleaseClaimCallCount { get; private set; }
    public Exception? SaveException { get; set; }
    public Exception? CloudCountException { get; set; }
    public Exception? MesCountException { get; set; }
    public TaskCompletionSource? CloudCountStarted { get; set; }
    public TaskCompletionSource? MesCountStarted { get; set; }
    public Task? CloudCountWait { get; set; }
    public Task? MesCountWait { get; set; }
    public Action? ClaimPendingBatchReturning { get; set; }
    public Action? DeleteReturning { get; set; }
    public Action? UpdateRetryReturning { get; set; }
    public Queue<Exception> SaveExceptions { get; } = new();
    public TaskCompletionSource? SaveStarted { get; set; }
    public Task? SaveWait { get; set; }
    public DateTime? LastDeleteExpiredOlderThanUtc { get; private set; }
    public int DeleteExpiredAbandonedResult { get; set; }

    public async Task SaveAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveStarted?.TrySetResult();
        if (SaveWait is not null)
        {
            await SaveWait.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await SaveAsync(record, failedTarget, errorMessage, failedTarget);
    }

    public Task SaveRawAsync(string processType, string cellDataJson, string failedTarget, string errorMessage)
        => SaveRawAsync(processType, cellDataJson, failedTarget, errorMessage, failedTarget);

    public Task<List<FailedCellRecord>> GetPendingAsync(int batchSize = 10)
        => GetPendingAsync(channel: null, batchSize);

    public Task<ClaimedFailedCellBatch?> ClaimPendingBatchAsync(int batchSize = 10)
        => ClaimPendingBatchAsync(channel: null, batchSize);

    public Task<int> GetCountAsync(string processType)
        => GetCountByProcessTypeAsync(processType);

    public Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage, string channel)
    {
        SaveCallCount++;

        if (SaveException is not null)
        {
            throw SaveException;
        }

        if (SaveExceptions.Count > 0)
        {
            throw SaveExceptions.Dequeue();
        }

        PendingRecords.Add(new FailedCellRecord
        {
            Id = PendingRecords.Count == 0 ? 1 : PendingRecords.Max(x => x.Id) + 1,
            Channel = channel,
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            ProcessType = record.CellData.ProcessType,
            CellDataJson = "{}",
            NextRetryTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            NetworkDeviceId = record.ResolveNetworkDeviceId(),
            DeviceName = record.ResolveDeviceName(),
            ModuleId = record.ModuleId,
            TaskKey = record.TaskKey,
            PlanSessionId = record.PlanSessionId,
            MainPlanCode = record.MainPlanCode,
            TraceBatchNumber = record.TraceBatchNumber
        });
        return Task.CompletedTask;
    }

    public Task SaveRawAsync(string processType, string cellDataJson, string failedTarget, string errorMessage, string channel)
    {
        SaveCallCount++;

        if (SaveException is not null)
        {
            throw SaveException;
        }

        if (SaveExceptions.Count > 0)
        {
            throw SaveExceptions.Dequeue();
        }

        PendingRecords.Add(new FailedCellRecord
        {
            Id = PendingRecords.Count == 0 ? 1 : PendingRecords.Max(x => x.Id) + 1,
            Channel = channel,
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            ProcessType = processType,
            CellDataJson = cellDataJson,
            NextRetryTime = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }

    public Task<List<FailedCellRecord>> GetPendingAsync(string? channel, int batchSize = 10)
    {
        var now = DateTime.UtcNow;
        var rows = PendingRecords
            .Where(r => (channel is null || r.Channel == channel) && r.NextRetryTime <= now)
            .OrderBy(r => r.Id)
            .Take(batchSize)
            .ToList();

        return Task.FromResult(rows);
    }

    public Task<ClaimedFailedCellBatch?> ClaimPendingBatchAsync(string? channel, int batchSize = 10)
    {
        ClaimCallCount++;
        var now = DateTime.UtcNow;
        var rows = PendingRecords
            .Where(r => (channel is null || r.Channel == channel) && r.NextRetryTime <= now && !_claimedRecordIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .Take(batchSize)
            .ToList();

        if (rows.Count == 0)
        {
            return Task.FromResult<ClaimedFailedCellBatch?>(null);
        }

        var claimToken = Guid.NewGuid().ToString("N");
        var ids = rows.Select(x => x.Id).ToList();
        _claims[claimToken] = ids;

        foreach (var id in ids)
        {
            _claimedRecordIds.Add(id);
        }

        var batch = new ClaimedFailedCellBatch
        {
            ClaimToken = claimToken,
            Records = rows
        };
        ClaimPendingBatchReturning?.Invoke();
        return Task.FromResult<ClaimedFailedCellBatch?>(batch);
    }

    public Task DeleteAsync(long id)
    {
        ClearClaimForRecord(id);
        DeletedIds.Add(id);
        PendingRecords.RemoveAll(x => x.Id == id);
        DeleteReturning?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteClaimedBatchAsync(string claimToken)
    {
        if (!_claims.TryGetValue(claimToken, out var ids) || ids.Count == 0)
        {
            throw new InvalidOperationException($"No claimed failed-record rows found for claim {claimToken}.");
        }

        DeletedClaimTokens.Add(claimToken);
        DeletedIds.AddRange(ids);
        PendingRecords.RemoveAll(x => ids.Contains(x.Id));
        ReleaseClaimCore(claimToken);
        return Task.CompletedTask;
    }

    public Task ReleaseClaimAsync(string claimToken)
    {
        ReleaseClaimCallCount++;
        if (!_claims.ContainsKey(claimToken))
        {
            throw new InvalidOperationException($"No failed-record claim exists for token {claimToken}.");
        }

        ReleasedClaimTokens.Add(claimToken);
        ReleaseClaimCore(claimToken);
        return Task.CompletedTask;
    }

    public Task UpdateRetryAsync(long id, int retryCount, string errorMessage, DateTime nextRetryTime)
    {
        ClearClaimForRecord(id);
        Updates[id] = new RetryUpdate(id, retryCount, errorMessage, nextRetryTime);
        UpdateRetryReturning?.Invoke();
        return Task.CompletedTask;
    }

    public async Task<int> GetCountAsync()
    {
        var channel = CountControlChannel;
        if (TryGetCountException(channel) is { } ex)
        {
            throw ex;
        }

        await MaybeWaitAsync(channel);
        return PendingRecords.Count;
    }

    public async Task<int> GetChannelCountAsync(string channel)
    {
        if (TryGetCountException(channel) is { } ex)
        {
            throw ex;
        }

        await MaybeWaitAsync(channel);
        return PendingRecords.Count(x => x.Channel == channel);
    }

    public async Task<int> GetChannelCountAsync(string channel, string processType)
    {
        if (TryGetCountException(channel) is { } ex)
        {
            throw ex;
        }

        await MaybeWaitAsync(channel);
        return PendingRecords.Count(x =>
            x.Channel == channel
            && string.Equals(x.ProcessType, processType, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<int> GetCountByProcessTypeAsync(string processType)
    {
        var channel = CountControlChannel;
        if (TryGetCountException(channel) is { } ex)
        {
            throw ex;
        }

        await MaybeWaitAsync(channel);
        return PendingRecords.Count(x =>
            string.Equals(x.ProcessType, processType, StringComparison.OrdinalIgnoreCase));
    }

    private string CountControlChannel =>
        MesCountException is not null || MesCountStarted is not null || MesCountWait is not null
            ? "MES"
            : "Cloud";

    public Task ResetAllAbandonedAsync()
    {
        ResetAllAbandonedCallCount++;
        foreach (var record in PendingRecords.Where(x => x.NextRetryTime == DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)))
        {
            record.NextRetryTime = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task<int> DeleteExpiredAbandonedAsync(DateTime olderThanUtc)
    {
        DeleteExpiredAbandonedCallCount++;
        LastDeleteExpiredOlderThanUtc = olderThanUtc;

        var deleted = PendingRecords.RemoveAll(x =>
            x.NextRetryTime == DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
            && x.CreatedAt < olderThanUtc);

        if (DeleteExpiredAbandonedResult > 0)
        {
            deleted = DeleteExpiredAbandonedResult;
        }

        return Task.FromResult(deleted);
    }

    private Exception? TryGetCountException(string channel)
    {
        return channel switch
        {
            "Cloud" => CloudCountException,
            "MES" => MesCountException,
            _ => null
        };
    }

    private async Task MaybeWaitAsync(string channel)
    {
        var started = channel switch
        {
            "Cloud" => CloudCountStarted,
            "MES" => MesCountStarted,
            _ => null
        };
        var wait = channel switch
        {
            "Cloud" => CloudCountWait,
            "MES" => MesCountWait,
            _ => null
        };

        started?.TrySetResult();
        if (wait is not null)
        {
            await wait;
        }
    }

    private void ClearClaimForRecord(long id)
    {
        _claimedRecordIds.Remove(id);
        foreach (var pair in _claims.ToList())
        {
            pair.Value.Remove(id);
            if (pair.Value.Count == 0)
            {
                _claims.Remove(pair.Key);
            }
        }
    }

    private void ReleaseClaimCore(string claimToken)
    {
        if (!_claims.TryGetValue(claimToken, out var ids))
        {
            return;
        }

        foreach (var id in ids)
        {
            _claimedRecordIds.Remove(id);
        }

        _claims.Remove(claimToken);
    }
}

public sealed class FakeCloudFallbackBufferStore : ICloudFallbackBufferStore
{
    public List<CloudFallbackRecord> Records { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public int SaveCallCount { get; private set; }
    public Exception? SaveException { get; set; }
    public Exception? CountException { get; set; }
    public TaskCompletionSource? SaveStarted { get; set; }
    public Task? SaveWait { get; set; }
    public FakeFailedRecordStore? RetryStore { get; set; }

    public async Task SaveAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveStarted?.TrySetResult();
        if (SaveWait is not null)
        {
            await SaveWait.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SaveCallCount++;

        if (SaveException is not null)
        {
            throw SaveException;
        }

        Records.Add(new CloudFallbackRecord
        {
            Id = Records.Count == 0 ? 1 : Records.Max(x => x.Id) + 1,
            ProcessType = record.CellData.ProcessType,
            CellDataJson = "{}",
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            CreatedAt = DateTime.UtcNow,
            NetworkDeviceId = record.ResolveNetworkDeviceId(),
            DeviceName = record.ResolveDeviceName(),
            ModuleId = record.ModuleId,
            TaskKey = record.TaskKey,
            PlanSessionId = record.PlanSessionId,
            MainPlanCode = record.MainPlanCode,
            TraceBatchNumber = record.TraceBatchNumber
        });

    }

    public Task<List<CloudFallbackRecord>> GetPendingAsync(int batchSize = 50)
        => Task.FromResult(Records.OrderBy(x => x.Id).Take(batchSize).ToList());

    public Task MovePendingToRetryAsync(IEnumerable<long> ids)
    {
        if (RetryStore is null)
        {
            throw new InvalidOperationException("RetryStore is not attached for cloud fallback recovery.");
        }

        var idList = ids.Distinct().ToList();
        var rows = Records
            .Where(x => idList.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToList();

        foreach (var row in rows)
        {
            RetryStore.PendingRecords.Add(new FailedCellRecord
            {
                Id = RetryStore.PendingRecords.Count == 0 ? 1 : RetryStore.PendingRecords.Max(x => x.Id) + 1,
                Channel = "Cloud",
                ProcessType = row.ProcessType,
                CellDataJson = row.CellDataJson,
                FailedTarget = row.FailedTarget,
                ErrorMessage = row.ErrorMessage,
                RetryCount = 0,
                NextRetryTime = DateTime.UtcNow,
                CreatedAt = row.CreatedAt,
                NetworkDeviceId = row.NetworkDeviceId,
                DeviceName = row.DeviceName,
                ModuleId = row.ModuleId,
                TaskKey = row.TaskKey,
                PlanSessionId = row.PlanSessionId,
                MainPlanCode = row.MainPlanCode,
                TraceBatchNumber = row.TraceBatchNumber
            });
        }

        DeletedIds.AddRange(rows.Select(x => x.Id));
        Records.RemoveAll(x => idList.Contains(x.Id));
        return Task.CompletedTask;
    }

    public Task DeleteBatchAsync(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        DeletedIds.AddRange(idList);
        Records.RemoveAll(x => idList.Contains(x.Id));
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync()
        => CountException is not null
            ? Task.FromException<int>(CountException)
            : Task.FromResult(Records.Count);

}

public sealed class FakeMesFallbackBufferStore : IMesFallbackBufferStore
{
    public List<MesFallbackRecord> Records { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public int SaveCallCount { get; private set; }
    public Exception? SaveException { get; set; }
    public Exception? CountException { get; set; }
    public TaskCompletionSource? SaveStarted { get; set; }
    public Task? SaveWait { get; set; }
    public FakeFailedRecordStore? RetryStore { get; set; }

    public async Task SaveAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveStarted?.TrySetResult();
        if (SaveWait is not null)
        {
            await SaveWait.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SaveCallCount++;

        if (SaveException is not null)
        {
            throw SaveException;
        }

        Records.Add(new MesFallbackRecord
        {
            Id = Records.Count == 0 ? 1 : Records.Max(x => x.Id) + 1,
            ProcessType = record.CellData.ProcessType,
            CellDataJson = "{}",
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            CreatedAt = DateTime.UtcNow,
            NetworkDeviceId = record.ResolveNetworkDeviceId(),
            DeviceName = record.ResolveDeviceName(),
            ModuleId = record.ModuleId,
            TaskKey = record.TaskKey,
            PlanSessionId = record.PlanSessionId,
            MainPlanCode = record.MainPlanCode,
            TraceBatchNumber = record.TraceBatchNumber
        });

    }

    public Task<List<MesFallbackRecord>> GetPendingAsync(int batchSize = 50)
        => Task.FromResult(Records.OrderBy(x => x.Id).Take(batchSize).ToList());

    public Task MovePendingToRetryAsync(IEnumerable<long> ids)
    {
        if (RetryStore is null)
        {
            throw new InvalidOperationException("RetryStore is not attached for MES fallback recovery.");
        }

        var idList = ids.Distinct().ToList();
        var rows = Records
            .Where(x => idList.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToList();

        foreach (var row in rows)
        {
            RetryStore.PendingRecords.Add(new FailedCellRecord
            {
                Id = RetryStore.PendingRecords.Count == 0 ? 1 : RetryStore.PendingRecords.Max(x => x.Id) + 1,
                Channel = "MES",
                ProcessType = row.ProcessType,
                CellDataJson = row.CellDataJson,
                FailedTarget = row.FailedTarget,
                ErrorMessage = row.ErrorMessage,
                RetryCount = 0,
                NextRetryTime = DateTime.UtcNow,
                CreatedAt = row.CreatedAt,
                NetworkDeviceId = row.NetworkDeviceId,
                DeviceName = row.DeviceName,
                ModuleId = row.ModuleId,
                TaskKey = row.TaskKey,
                PlanSessionId = row.PlanSessionId,
                MainPlanCode = row.MainPlanCode,
                TraceBatchNumber = row.TraceBatchNumber
            });
        }

        DeletedIds.AddRange(rows.Select(x => x.Id));
        Records.RemoveAll(x => idList.Contains(x.Id));
        return Task.CompletedTask;
    }

    public Task DeleteBatchAsync(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        DeletedIds.AddRange(idList);
        Records.RemoveAll(x => idList.Contains(x.Id));
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync()
        => CountException is not null
            ? Task.FromException<int>(CountException)
            : Task.FromResult(Records.Count);

}

public sealed class FakeCloudDeadLetterStore : ICloudDeadLetterStore
{
    public List<DeadLetterRecord> Records { get; } = new();
    public Exception? SaveException { get; set; }
    public Exception? DeleteException { get; set; }
    public int SaveCallCount { get; private set; }
    public TaskCompletionSource? SaveStarted { get; set; }
    public Task? SaveWait { get; set; }
    public Action? SaveReturning { get; set; }

    public async Task SaveAsync(
        DeadLetterRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveStarted?.TrySetResult();
        if (SaveWait is not null)
        {
            await SaveWait.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SaveCallCount++;
        if (SaveException is not null)
        {
            throw SaveException;
        }

        Records.Add(record);
        SaveReturning?.Invoke();
    }

    public Task<int> GetCountAsync() => Task.FromResult(Records.Count);

    public Task<DeadLetterRecord?> GetByIdAsync(long id)
        => Task.FromResult(Records.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<DeadLetterGroupSummary>> GetGroupSummaryAsync()
        => Task.FromResult<IReadOnlyList<DeadLetterGroupSummary>>(DeadLetterTestHelpers.BuildGroupSummary(Records));

    public Task<IReadOnlyList<DeadLetterRecord>> GetLatestAsync(int count = 20)
        => Task.FromResult<IReadOnlyList<DeadLetterRecord>>(DeadLetterTestHelpers.GetLatest(Records, count));

    public Task DeleteAsync(long id)
    {
        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        Records.RemoveAll(x => x.Id == id);
        return Task.CompletedTask;
    }
}

public sealed class FakeMesDeadLetterStore : IMesDeadLetterStore
{
    public List<DeadLetterRecord> Records { get; } = new();
    public Exception? SaveException { get; set; }
    public Exception? DeleteException { get; set; }
    public int SaveCallCount { get; private set; }
    public TaskCompletionSource? SaveStarted { get; set; }
    public Task? SaveWait { get; set; }
    public Action? SaveReturning { get; set; }

    public async Task SaveAsync(
        DeadLetterRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveStarted?.TrySetResult();
        if (SaveWait is not null)
        {
            await SaveWait.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SaveCallCount++;
        if (SaveException is not null)
        {
            throw SaveException;
        }

        Records.Add(record);
        SaveReturning?.Invoke();
    }

    public Task<int> GetCountAsync() => Task.FromResult(Records.Count);

    public Task<DeadLetterRecord?> GetByIdAsync(long id)
        => Task.FromResult(Records.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<DeadLetterGroupSummary>> GetGroupSummaryAsync()
        => Task.FromResult<IReadOnlyList<DeadLetterGroupSummary>>(DeadLetterTestHelpers.BuildGroupSummary(Records));

    public Task<IReadOnlyList<DeadLetterRecord>> GetLatestAsync(int count = 20)
        => Task.FromResult<IReadOnlyList<DeadLetterRecord>>(DeadLetterTestHelpers.GetLatest(Records, count));

    public Task DeleteAsync(long id)
    {
        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        Records.RemoveAll(x => x.Id == id);
        return Task.CompletedTask;
    }
}

public static class DeadLetterTestHelpers
{
    public static List<DeadLetterGroupSummary> BuildGroupSummary(IEnumerable<DeadLetterRecord> records)
        => records
            .GroupBy(x => new { x.ProcessType, x.FailureStage })
            .Select(x => new DeadLetterGroupSummary
            {
                ProcessType = x.Key.ProcessType,
                FailureStage = x.Key.FailureStage,
                Count = x.Count(),
                LastCreatedAt = x.Max(y => y.CreatedAt)
            })
            .ToList();

    public static List<DeadLetterRecord> GetLatest(IEnumerable<DeadLetterRecord> records, int count)
        => records
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(count)
            .ToList();
}

public sealed class FakeCriticalPersistenceFallbackWriter : ICriticalPersistenceFallbackWriter
{
    public sealed record WriteEntry(string Source, string Details, Exception? Exception);

    public List<WriteEntry> Writes { get; } = new();
    public Exception? WriteException { get; set; }

    public void Write(string source, string details, Exception? exception = null)
    {
        if (WriteException is not null)
        {
            throw WriteException;
        }

        Writes.Add(new WriteEntry(source, details, exception));
    }
}

public sealed class FakeIngressOverflowPersistence : IIngressOverflowPersistence
{
    public List<CellCompletedRecord> Records { get; } = new();
    public DataPipelineEnqueueResult Result { get; set; } = DataPipelineEnqueueResult.OverflowPersisted(1, 0);

    public ValueTask<DataPipelineEnqueueResult> PersistOverflowAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default)
    {
        Records.Add(record);
        return ValueTask.FromResult(Result);
    }
}

public sealed class FakeDeviceLogBufferStore : IDeviceLogBufferStore
{
    private readonly Dictionary<string, List<long>> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _claimedRecordIds = new();

    public List<DeviceLogRecord> Records { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public List<string> DeletedClaimTokens { get; } = new();
    public List<string> ReleasedClaimTokens { get; } = new();
    public Exception? SaveBatchException { get; set; }
    public Exception? CountException { get; set; }
    public TaskCompletionSource? CountStarted { get; set; }
    public Task? CountWait { get; set; }
    public Action? ClaimPendingBatchReturning { get; set; }
    public Exception? DeleteClaimedBatchException { get; set; }
    public Action? DeleteClaimedBatchReturning { get; set; }
    public Action? ReleaseClaimReturning { get; set; }

    public Task SaveBatchAsync(IEnumerable<DeviceLogRecord> records)
    {
        if (SaveBatchException is not null)
        {
            throw SaveBatchException;
        }

        var nextId = Records.Count == 0 ? 1 : Records.Max(x => x.Id) + 1;
        foreach (var record in records)
        {
            var copy = new DeviceLogRecord
            {
                Id = record.Id == 0 ? nextId++ : record.Id,
                Level = record.Level,
                Message = record.Message,
                LogTime = record.LogTime,
                CreatedAt = record.CreatedAt
            };

            Records.Add(copy);
        }

        return Task.CompletedTask;
    }

    public Task<List<DeviceLogRecord>> GetPendingAsync(int batchSize = 100)
    {
        var rows = Records
            .OrderBy(x => x.Id)
            .Take(batchSize)
            .ToList();

        return Task.FromResult(rows);
    }

    public Task<ClaimedDeviceLogBatch?> ClaimPendingBatchAsync(int batchSize = 100)
    {
        var rows = Records
            .Where(x => !_claimedRecordIds.Contains(x.Id))
            .OrderBy(x => x.Id)
            .Take(batchSize)
            .ToList();

        if (rows.Count == 0)
        {
            return Task.FromResult<ClaimedDeviceLogBatch?>(null);
        }

        var claimToken = Guid.NewGuid().ToString("N");
        var ids = rows.Select(x => x.Id).ToList();
        _claims[claimToken] = ids;
        foreach (var id in ids)
        {
            _claimedRecordIds.Add(id);
        }

        var batch = new ClaimedDeviceLogBatch
        {
            ClaimToken = claimToken,
            Records = rows.Select(CloneDeviceLogRecord).ToList()
        };
        ClaimPendingBatchReturning?.Invoke();
        return Task.FromResult<ClaimedDeviceLogBatch?>(batch);
    }

    public Task DeleteClaimedBatchAsync(string claimToken)
    {
        if (DeleteClaimedBatchException is not null)
        {
            throw DeleteClaimedBatchException;
        }

        DeletedClaimTokens.Add(claimToken);

        if (_claims.TryGetValue(claimToken, out var ids))
        {
            DeletedIds.AddRange(ids);
            Records.RemoveAll(x => ids.Contains(x.Id));
            foreach (var id in ids)
            {
                _claimedRecordIds.Remove(id);
            }

            _claims.Remove(claimToken);
        }

        DeleteClaimedBatchReturning?.Invoke();
        return Task.CompletedTask;
    }

    public Task ReleaseClaimAsync(string claimToken)
    {
        ReleasedClaimTokens.Add(claimToken);

        if (_claims.TryGetValue(claimToken, out var ids))
        {
            foreach (var id in ids)
            {
                _claimedRecordIds.Remove(id);
            }

            _claims.Remove(claimToken);
        }

        ReleaseClaimReturning?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteBatchAsync(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        DeletedIds.AddRange(idList);
        Records.RemoveAll(x => idList.Contains(x.Id));
        return Task.CompletedTask;
    }

    public async Task<int> GetCountAsync()
    {
        if (CountException is not null)
        {
            throw CountException;
        }

        CountStarted?.TrySetResult();
        if (CountWait is not null)
        {
            await CountWait;
        }

        return Records.Count;
    }

    private static DeviceLogRecord CloneDeviceLogRecord(DeviceLogRecord source)
        => new()
        {
            Id = source.Id,
            Level = source.Level,
            Message = source.Message,
            LogTime = source.LogTime,
            CreatedAt = source.CreatedAt
        };
}

public sealed class FakeCloudHttpClient : ICloudHttpClient
{
    private readonly Queue<CloudCallResult> _postResults = new();
    private readonly Queue<CloudCallResult<string>> _postWithResponseResults = new();
    private readonly Queue<CloudCallResult<string>> _getResults = new();
    private int _activePostCount;
    private int _completedPostCount;
    private int _completedGetCount;

    public int PostCallCount { get; private set; }
    public int CompletedPostCount => Volatile.Read(ref _completedPostCount);
    public int GetCallCount { get; private set; }
    public int CompletedGetCount => Volatile.Read(ref _completedGetCount);
    public int MaxConcurrentPostCount { get; private set; }
    public string? LastPostUrl { get; private set; }
    public object? LastPayload { get; private set; }
    public CloudRequestOptions? LastPostOptions { get; private set; }
    public CloudRequestOptions? LastGetOptions { get; private set; }
    public List<string> PostUrls { get; } = new();
    public List<object> PostPayloads { get; } = new();
    public List<string?> PostIdempotencyKeys { get; } = new();
    public List<CancellationToken> PostCancellationTokens { get; } = new();
    public List<string?> GetIdempotencyKeys { get; } = new();
    public List<string> GetUrls { get; } = new();
    public TaskCompletionSource? PostStarted { get; set; }
    public Task? PostWait { get; set; }
    public Action? PostReturning { get; set; }
    public TaskCompletionSource? GetStarted { get; set; }
    public Task? GetWait { get; set; }
    public List<CancellationToken> GetCancellationTokens { get; } = new();
    public Exception? GetException { get; set; }

    public void EnqueuePostResult(bool result)
        => _postResults.Enqueue(
            result
                ? CloudCallResult.Success()
                : CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "fake_http_failure"));

    public void EnqueuePostResult(CloudCallResult result) => _postResults.Enqueue(result);

    public void EnqueuePostWithResponseResult(CloudCallResult<string> result)
        => _postWithResponseResults.Enqueue(result);

    public void EnqueueGetResult(CloudCallResult<string> result)
        => _getResults.Enqueue(result);

    public async Task<CloudCallResult> PostAsync(
        string url,
        object payload,
        CloudRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activePostCount = Interlocked.Increment(ref _activePostCount);
        MaxConcurrentPostCount = Math.Max(MaxConcurrentPostCount, activePostCount);
        PostCallCount++;
        LastPostUrl = url;
        LastPayload = payload;
        LastPostOptions = options;
        PostUrls.Add(url);
        PostPayloads.Add(payload);
        PostIdempotencyKeys.Add(options?.IdempotencyKey);
        PostCancellationTokens.Add(cancellationToken);
        PostStarted?.TrySetResult();

        try
        {
            if (PostWait is not null)
            {
                await PostWait.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Increment(ref _completedPostCount);

            if (_postResults.Count > 0)
            {
                var result = _postResults.Dequeue();
                PostReturning?.Invoke();
                return result;
            }

            PostReturning?.Invoke();
            return CloudCallResult.Success();
        }
        finally
        {
            Interlocked.Decrement(ref _activePostCount);
        }
    }

    public Task<CloudCallResult<string>> PostWithResponseAsync(
        string url,
        object payload,
        CloudRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PostCallCount++;
        LastPostUrl = url;
        LastPayload = payload;
        LastPostOptions = options;
        PostUrls.Add(url);
        PostPayloads.Add(payload);
        PostIdempotencyKeys.Add(options?.IdempotencyKey);
        PostCancellationTokens.Add(cancellationToken);

        if (_postWithResponseResults.Count > 0)
        {
            return Task.FromResult(_postWithResponseResults.Dequeue());
        }

        return Task.FromResult(CloudCallResult<string>.Success(null));
    }

    public async Task<CloudCallResult<string>> GetAsync(
        string url,
        CloudRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetException is not null)
        {
            throw GetException;
        }

        GetCallCount++;
        LastGetOptions = options;
        GetUrls.Add(url);
        GetIdempotencyKeys.Add(options?.IdempotencyKey);
        GetCancellationTokens.Add(cancellationToken);
        GetStarted?.TrySetResult();

        if (GetWait is not null)
        {
            await GetWait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        Interlocked.Increment(ref _completedGetCount);

        if (_getResults.Count > 0)
        {
            return _getResults.Dequeue();
        }

        return CloudCallResult<string>.Success(null);
    }
}

public sealed class FakeRecipeService : IRecipeService
{
    public RecipeSource ActiveSource { get; private set; } = RecipeSource.Cloud;
    public RecipeData? ActiveRecipe => ActiveSource == RecipeSource.Cloud ? CloudRecipe : LocalRecipe;
    public RecipeData? CloudRecipe { get; set; }
    public RecipeData? LocalRecipe { get; set; }
    public int PullFromCloudCallCount { get; private set; }
    public CancellationToken LastPullCancellationToken { get; private set; }
    public Func<CancellationToken, Task<bool>>? PullFromCloudHandler { get; set; }
    public Action<string, double?, double?, string>? SetLocalParamHandler { get; set; }
    public Action<string>? RemoveLocalParamHandler { get; set; }

    public event Action? RecipeChanged;

    public void SwitchSource(RecipeSource source)
    {
        ActiveSource = source;
        RecipeChanged?.Invoke();
    }

    public RecipeParam? GetParam(string name)
        => GetAllParams().TryGetValue(name, out var parameter) ? parameter : null;

    public IReadOnlyDictionary<string, RecipeParam> GetAllParams()
        => ActiveRecipe?.Parameters ?? new Dictionary<string, RecipeParam>();

    public async Task<bool> PullFromCloudAsync(CancellationToken cancellationToken = default)
    {
        PullFromCloudCallCount++;
        LastPullCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        return PullFromCloudHandler is null
            ? false
            : await PullFromCloudHandler(cancellationToken).ConfigureAwait(false);
    }

    public void SetLocalParam(string name, double? min, double? max, string unit)
    {
        SetLocalParamHandler?.Invoke(name, min, max, unit);
    }

    public void RemoveLocalParam(string name)
    {
        RemoveLocalParamHandler?.Invoke(name);
    }

    public void LoadFromFile()
    {
    }

    public void SaveToFile()
    {
    }
}

public sealed class FakeDeviceAccessTokenProvider(string? accessToken = null) : IDeviceAccessTokenProvider
{
    public string? AccessToken { get; set; } = accessToken;
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
}

public sealed class FakeCapacityBufferStore : ICapacityBufferStore
{
    private readonly Dictionary<string, List<BufferHourlySummaryDto>> _claims = new(StringComparer.OrdinalIgnoreCase);

    public List<CapacityRecord> Records { get; } = new();
    public List<BufferHourlySummaryDto> HourlySummaries { get; } = new();
    public List<string> ReleasedClaimTokens { get; } = new();
    public List<int> ClaimBatchSizes { get; } = new();
    public List<(string ClaimToken, string Date, int Hour, int MinuteBucket, string ShiftCode, string PlcName)> DeletedSummaries { get; } = new();
    public int ClearAllCallCount { get; private set; }
    public Exception? CountException { get; set; }
    public TaskCompletionSource? CountStarted { get; set; }
    public Task? CountWait { get; set; }

    public Task SaveAsync(CapacityRecord record)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }

    public Task SaveBatchAsync(IEnumerable<CapacityRecord> records)
    {
        Records.AddRange(records);
        return Task.CompletedTask;
    }

    public Task<List<BufferHourlySummaryDto>> GetHourlySummaryAsync()
        => Task.FromResult(HourlySummaries.Select(CloneHourlySummary).ToList());

    public Task<ClaimedCapacityBufferBatch?> ClaimHourlySummaryBatchAsync(int batchSize = 200)
    {
        ClaimBatchSizes.Add(batchSize);
        var rows = HourlySummaries
            .Take(batchSize)
            .Select(CloneHourlySummary)
            .ToList();

        if (rows.Count == 0)
        {
            return Task.FromResult<ClaimedCapacityBufferBatch?>(null);
        }

        var claimToken = Guid.NewGuid().ToString("N");
        _claims[claimToken] = rows.Select(CloneHourlySummary).ToList();

        return Task.FromResult<ClaimedCapacityBufferBatch?>(new ClaimedCapacityBufferBatch
        {
            ClaimToken = claimToken,
            Summaries = rows
        });
    }

    public Task DeleteClaimedSummaryAsync(
        string claimToken,
        string date,
        int hour,
        int minuteBucket,
        string shiftCode,
        string plcName)
    {
        DeletedSummaries.Add((claimToken, date, hour, minuteBucket, shiftCode, plcName));

        HourlySummaries.RemoveAll(x =>
            x.Date == date
            && x.Hour == hour
            && x.MinuteBucket == minuteBucket
            && x.ShiftCode == shiftCode
            && x.PlcName == plcName);

        if (_claims.TryGetValue(claimToken, out var claimed))
        {
            claimed.RemoveAll(x =>
                x.Date == date
                && x.Hour == hour
                && x.MinuteBucket == minuteBucket
                && x.ShiftCode == shiftCode
                && x.PlcName == plcName);

            if (claimed.Count == 0)
            {
                _claims.Remove(claimToken);
            }
        }

        return Task.CompletedTask;
    }

    public Task ReleaseClaimAsync(string claimToken)
    {
        ReleasedClaimTokens.Add(claimToken);
        _claims.Remove(claimToken);
        return Task.CompletedTask;
    }

    public Task ClearAllAsync()
    {
        ClearAllCallCount++;
        HourlySummaries.Clear();
        Records.Clear();
        _claims.Clear();
        return Task.CompletedTask;
    }

    public async Task<int> GetCountAsync()
    {
        if (CountException is not null)
        {
            throw CountException;
        }

        CountStarted?.TrySetResult();
        if (CountWait is not null)
        {
            await CountWait;
        }

        return Records.Count;
    }

    private static BufferHourlySummaryDto CloneHourlySummary(BufferHourlySummaryDto source)
        => new()
        {
            Date = source.Date,
            Hour = source.Hour,
            MinuteBucket = source.MinuteBucket,
            ShiftCode = source.ShiftCode,
            Total = source.Total,
            OkCount = source.OkCount,
            NgCount = source.NgCount,
            PlcName = source.PlcName
        };
}

public sealed class FakeProductionContextStore : IProductionContextStore
{
    private readonly Dictionary<string, ProductionContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

    public ProductionContextPersistenceDiagnostics PersistenceDiagnostics { get; set; } = new(0, null);

    public ProductionContext GetOrCreate(string deviceName)
        => GetOrCreate(deviceName, moduleId: null);

    public ProductionContext GetOrCreate(string deviceName, string? moduleId)
    {
        if (!_contexts.TryGetValue(deviceName, out var context))
        {
            context = new ProductionContext { DeviceName = deviceName };
            _contexts[deviceName] = context;
        }

        return context;
    }

    public IReadOnlyCollection<ProductionContext> GetAll() => _contexts.Values.ToList().AsReadOnly();

    public ProductionContextPersistenceDiagnostics GetPersistenceDiagnostics() => PersistenceDiagnostics;

    public void LoadFromFile()
    {
    }

    public void SaveToFile()
    {
    }

    public Task StartAutoSaveAsync(CancellationToken ct, int intervalSeconds = 30) => Task.CompletedTask;
}

public sealed class FakeCloudBatchConsumer : ICloudBatchConsumer
{
    private readonly Queue<CloudCallResult> _results = new();

    public int ProcessBatchCallCount { get; private set; }
    public List<IReadOnlyList<CellCompletedRecord>> ReceivedBatches { get; } = new();
    public Func<CellCompletedRecord, CloudCallResult>? ValidateRecord { get; set; }

    public void EnqueueResult(bool result)
        => _results.Enqueue(
            result
                ? CloudCallResult.Success()
                : CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "fake_batch_failure"));

    public void EnqueueResult(CloudCallResult result) => _results.Enqueue(result);

    public CloudCallResult ValidateBatchRecord(CellCompletedRecord record)
        => ValidateRecord?.Invoke(record) ?? CloudCallResult.Success();

    public Task<CloudCallResult> ProcessBatchAsync(
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessBatchCallCount++;
        ReceivedBatches.Add(records.ToList());

        if (_results.Count > 0)
        {
            return Task.FromResult(_results.Dequeue());
        }

        return Task.FromResult(CloudCallResult.Success());
    }
}

public sealed class FakeCloudDiagnosticsStore : ICloudUploadDiagnosticsStore
{
    public int RecordResultCallCount { get; private set; }
    public CloudUploadDiagnosticsSnapshot Snapshot { get; private set; } = new(
        LastAttemptAt: null,
        LastSuccessAt: null,
        LastFailureAt: null,
        LastBlockedAt: null,
        LastOutcome: CloudCallOutcome.Success,
        LastReasonCode: "none",
        LastBlockedReason: null,
        LastProcessType: null,
        RuntimeState: CloudRetryRuntimeState.Idle,
        IsCapacityBlocked: false,
        BlockedChannel: null,
        BlockedReason: "none",
        LastCapacityBlockAt: null);

    public void RecordResult(
        string? processType,
        CloudCallResult result,
        CloudUploadDiagnosticsContext? context = null)
    {
        RecordResultCallCount++;
        var now = DateTime.UtcNow;
        var isBlocked = result.Outcome == CloudCallOutcome.SkippedUploadNotReady;
        var isFailure = !result.IsSuccess && !isBlocked;
        var normalizedReasonCode = string.IsNullOrWhiteSpace(result.ReasonCode)
            ? "unknown"
            : result.ReasonCode.Trim();
        Snapshot = Snapshot with
        {
            LastAttemptAt = now,
            LastSuccessAt = result.IsSuccess ? now : Snapshot.LastSuccessAt,
            LastFailureAt = isFailure ? now : Snapshot.LastFailureAt,
            LastBlockedAt = isBlocked ? now : null,
            LastOutcome = result.Outcome,
            LastReasonCode = normalizedReasonCode,
            LastBlockedReason = isBlocked ? normalizedReasonCode : null,
            LastProcessType = processType,
            LastDeviceName = context?.DeviceName,
            LastModuleId = context?.ModuleId,
            LastTaskKey = context?.TaskKey,
            LastScenario = context?.Scenario
        };
    }

    public void RecordBlocked(
        string? processType,
        string reasonCode,
        string? blockedReason = null,
        CloudUploadDiagnosticsContext? context = null)
    {
        var normalizedReasonCode = NormalizeReasonCode(reasonCode);
        var normalizedReason = NormalizeReason(blockedReason, normalizedReasonCode);
        var now = DateTime.UtcNow;
        Snapshot = Snapshot with
        {
            LastAttemptAt = now,
            LastBlockedAt = now,
            LastOutcome = CloudCallOutcome.SkippedUploadNotReady,
            LastReasonCode = normalizedReasonCode,
            LastBlockedReason = normalizedReason,
            LastProcessType = processType,
            LastDeviceName = context?.DeviceName,
            LastModuleId = context?.ModuleId,
            LastTaskKey = context?.TaskKey,
            LastScenario = context?.Scenario
        };
    }

    public void SetRuntimeState(CloudRetryRuntimeState state)
    {
        Snapshot = Snapshot with
        {
            RuntimeState = state
        };
    }

    public void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null)
    {
        Snapshot = Snapshot with
        {
            IsCapacityBlocked = true,
            BlockedChannel = channel,
            BlockedReason = blockedReason,
            LastCapacityBlockAt = occurredAt ?? DateTime.UtcNow
        };
    }

    public void ClearCapacityBlocked()
    {
        Snapshot = Snapshot with
        {
            IsCapacityBlocked = false,
            BlockedChannel = null,
            BlockedReason = "none"
        };
    }

    private static string NormalizeReasonCode(string? reasonCode)
        => string.IsNullOrWhiteSpace(reasonCode) ? "cloud_upload_blocked" : reasonCode.Trim();

    private static string NormalizeReason(string? reason, string fallback)
        => string.IsNullOrWhiteSpace(reason) ? fallback : reason.Trim();
}

public sealed class FakeMesRetryDiagnosticsStore : IMesRetryDiagnosticsStore
{
    public MesRetryDiagnosticsSnapshot Snapshot { get; private set; } = new(
        MesRetryRuntimeState.Idle,
        IsCapacityBlocked: false,
        BlockedChannel: null,
        BlockedReason: "none",
        LastCapacityBlockAt: null);

    public void SetRuntimeState(MesRetryRuntimeState state)
    {
        Snapshot = Snapshot with
        {
            RuntimeState = state
        };
    }

    public void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null)
    {
        Snapshot = Snapshot with
        {
            IsCapacityBlocked = true,
            BlockedChannel = channel,
            BlockedReason = blockedReason,
            LastCapacityBlockAt = occurredAt ?? DateTime.UtcNow
        };
    }

    public void ClearCapacityBlocked()
    {
        Snapshot = Snapshot with
        {
            IsCapacityBlocked = false,
            BlockedChannel = null,
            BlockedReason = "none"
        };
    }
}

public sealed class FakeCloudConsumer : ICloudConsumer
{
    private readonly Queue<CloudCallResult> _results = new();

    public string Name { get; init; } = "Cloud";
    public int Order { get; init; } = 20;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;
    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Cloud;
    public int ProcessCallCount { get; private set; }
    public List<CellCompletedRecord> ProcessedRecords { get; } = new();
    public TaskCompletionSource? ProcessStarted { get; set; }
    public Task? ProcessWait { get; set; }

    public void EnqueueResult(bool success)
        => _results.Enqueue(
            success
                ? CloudCallResult.Success()
                : CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "fake_cloud_failure"));

    public void EnqueueResult(CloudCallResult result) => _results.Enqueue(result);

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
        => (await ProcessWithResultAsync(record, cancellationToken).ConfigureAwait(false)).IsSuccess;

    public async Task<CloudCallResult> ProcessWithResultAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessCallCount++;
        ProcessedRecords.Add(record);
        ProcessStarted?.TrySetResult();
        if (ProcessWait is not null)
        {
            await ProcessWait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_results.Count > 0)
        {
            return _results.Dequeue();
        }

        return CloudCallResult.Success();
    }
}

public sealed class FakeMesConsumer : IMesConsumer
{
    private readonly Queue<bool> _results = new();

    public string Name { get; init; } = "MES";
    public int Order { get; init; } = 30;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;
    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Mes;
    public int ProcessCallCount { get; private set; }
    public List<CellCompletedRecord> ProcessedRecords { get; } = new();
    public TaskCompletionSource? ProcessStarted { get; set; }
    public Task? ProcessWait { get; set; }

    public void EnqueueResult(bool success) => _results.Enqueue(success);

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessCallCount++;
        ProcessedRecords.Add(record);
        ProcessStarted?.TrySetResult();
        if (ProcessWait is not null)
        {
            await ProcessWait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_results.Count > 0)
        {
            return _results.Dequeue();
        }

        return true;
    }
}

public sealed class FakeDeviceLogSyncTask : IDeviceLogSyncTask
{
    public int RetryBufferCallCount { get; private set; }
    public bool RetryResult { get; set; } = true;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task<bool> RetryBufferAsync()
    {
        RetryBufferCallCount++;
        return Task.FromResult(RetryResult);
    }
}

public sealed class FakeCapacitySyncTask : ICapacitySyncTask
{
    public int RetryBufferCallCount { get; private set; }
    public bool RetryResult { get; set; } = true;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task<bool> RetryBufferAsync()
    {
        RetryBufferCallCount++;
        return Task.FromResult(RetryResult);
    }
}

public sealed class FakeMesUploadDiagnosticsStore : IMesUploadDiagnosticsStore
{
    private readonly Dictionary<string, MesChannelDiagnostics> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MesChannelDiagnostics> GetAll()
        => _entries.Values.OrderBy(x => x.ProcessType, StringComparer.OrdinalIgnoreCase).ToArray();

    public MesChannelDiagnostics? Get(string processType)
    {
        if (_entries.TryGetValue(processType, out var diagnostics))
        {
            return diagnostics;
        }

        var matches = _entries.Values
            .Where(x => string.Equals(x.ProcessType, processType, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public void RecordSuccess(string processType, MesUploadDiagnosticsContext? context = null)
    {
        var now = DateTime.UtcNow;
        _entries[BuildKey(processType, context)] = CreateDiagnostics(
            processType,
            now,
            now,
            "Success",
            null,
            context);
    }

    public void RecordFailure(string processType, string failureReason, MesUploadDiagnosticsContext? context = null)
    {
        var now = DateTime.UtcNow;
        var key = BuildKey(processType, context);
        var lastSuccessAt = _entries.TryGetValue(key, out var existing)
            ? existing.LastSuccessAt
            : null;

        _entries[key] = CreateDiagnostics(
            processType,
            now,
            lastSuccessAt,
            "Failed",
            failureReason,
            context);
    }

    public void RecordBlocked(string processType, string blockedReason, MesUploadDiagnosticsContext? context = null)
    {
        var now = DateTime.UtcNow;
        var key = BuildKey(processType, context);
        var lastSuccessAt = _entries.TryGetValue(key, out var existing)
            ? existing.LastSuccessAt
            : null;

        _entries[key] = CreateDiagnostics(
            processType,
            now,
            lastSuccessAt,
            "Blocked",
            null,
            context,
            LastBlockedAt: now,
            LastBlockedReason: blockedReason);
    }

    private static MesChannelDiagnostics CreateDiagnostics(
        string processType,
        DateTime? lastAttemptAt,
        DateTime? lastSuccessAt,
        string lastResult,
        string? lastFailureReason,
        MesUploadDiagnosticsContext? context,
        DateTime? LastBlockedAt = null,
        string? LastBlockedReason = null)
        => new(
            processType,
            lastAttemptAt,
            lastSuccessAt,
            lastResult,
            lastFailureReason,
            LastBlockedAt: LastBlockedAt,
            LastBlockedReason: LastBlockedReason,
            DeviceName: Normalize(context?.DeviceName),
            ModuleId: Normalize(context?.ModuleId),
            TaskKey: Normalize(context?.TaskKey),
            Scenario: Normalize(context?.Scenario));

    private static string BuildKey(string processType, MesUploadDiagnosticsContext? context)
    {
        var deviceName = Normalize(context?.DeviceName);
        var taskKey = Normalize(context?.TaskKey);
        return deviceName is null && taskKey is null
            ? processType
            : $"{processType}|{deviceName ?? string.Empty}|{taskKey ?? string.Empty}";
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class FakeMesUploader : IProcessMesUploader
{
    private readonly Queue<MesCallResult> _results = new();

    public FakeMesUploader(string processType, ProcessUploadMode uploadMode = ProcessUploadMode.Single)
    {
        ProcessType = processType;
        UploadMode = uploadMode;
    }

    public string ProcessType { get; }

    public ProcessUploadMode UploadMode { get; }

    public int UploadCallCount { get; private set; }

    public List<ProcessUploadContext> UploadedContexts { get; } = new();

    public ProcessUploadContext? LastUploadContext
        => UploadedContexts.Count == 0 ? null : UploadedContexts[^1];

    public List<IReadOnlyList<CellCompletedRecord>> UploadedBatches { get; } = new();

    public void EnqueueResult(bool result)
        => _results.Enqueue(result ? MesCallResult.Success() : MesCallResult.TransportFailure("fake_mes_failure"));

    public void EnqueueResult(MesCallResult result) => _results.Enqueue(result);

    public Task<MesCallResult> UploadAsync(
        ProcessUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        UploadCallCount++;
        UploadedContexts.Add(context);
        UploadedBatches.Add(records.ToList());

        if (_results.Count > 0)
        {
            return Task.FromResult(_results.Dequeue());
        }

        return Task.FromResult(MesCallResult.Success());
    }
}

public sealed class FakeProcessIntegrationRegistry : IProcessIntegrationRegistry
{
    private readonly Dictionary<string, ProcessUploaderRegistration> _cloud = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProcessUploaderRegistration> _mes = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode)
        => _cloud[processType] = new ProcessUploaderRegistration(processType, uploadMode);

    public void RegisterMesUploader(string processType, ProcessUploadMode uploadMode)
        => _mes[processType] = new ProcessUploaderRegistration(processType, uploadMode);

    public bool HasCloudUploader(string processType) => _cloud.ContainsKey(processType);

    public bool HasMesUploader(string processType) => _mes.ContainsKey(processType);

    public bool TryGetCloudUploader(string processType, out ProcessUploaderRegistration registration)
        => _cloud.TryGetValue(processType, out registration!);

    public bool TryGetMesUploader(string processType, out ProcessUploaderRegistration registration)
        => _mes.TryGetValue(processType, out registration!);

    public IReadOnlyDictionary<string, ProcessUploaderRegistration> GetCloudUploaders() => _cloud;

    public IReadOnlyDictionary<string, ProcessUploaderRegistration> GetMesUploaders() => _mes;
}

public sealed class FakeModuleParamRoleProvider : IModuleParamRoleProvider
{
    public bool MesEnabled { get; set; } = true;

    public Task<ModuleParamRoleValue?> GetAsync(
        string moduleId,
        ModuleParamCategory category,
        ModuleParamRole role,
        CancellationToken cancellationToken = default)
        => Task.FromResult<ModuleParamRoleValue?>(null);

    public Task<IReadOnlyList<ModuleParamRoleValue>> GetAllAsync(
        ModuleParamCategory category,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ModuleParamRoleValue>>([]);

    public Task<string?> GetStringAsync(
        string moduleId,
        ModuleParamCategory category,
        ModuleParamRole role,
        string? defaultValue = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(defaultValue);

    public Task<string?> FirstStringAsync(
        ModuleParamCategory category,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<bool> GetBoolAsync(
        string moduleId,
        ModuleParamCategory category,
        ModuleParamRole role,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult(role == ModuleParamRole.MesEnabled ? MesEnabled : defaultValue);

    public Task<bool> AnyBoolAsync(
        ModuleParamCategory category,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult((moduleIds?.Count ?? 0) > 0
            && role == ModuleParamRole.MesEnabled
            && MesEnabled);
}
