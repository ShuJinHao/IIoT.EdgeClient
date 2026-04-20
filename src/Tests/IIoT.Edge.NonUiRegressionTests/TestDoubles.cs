using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.DeviceLog;

namespace IIoT.Edge.NonUiRegressionTests;

internal sealed class FakeLogService : ILogService
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

internal sealed class FakeDeviceService : IDeviceService, IDeviceAccessTokenProvider
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

internal sealed class FakeDataPipelineService : IDataPipelineService
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

internal sealed class FakeCellDataConsumer : ICellDataConsumer
{
    private readonly bool _result;
    private readonly Func<CellCompletedRecord, Task<bool>>? _processAsync;

    public FakeCellDataConsumer(
        string name,
        int order,
        string? retryChannel,
        bool result,
        ConsumerFailureMode failureMode = ConsumerFailureMode.BestEffort,
        Func<CellCompletedRecord, Task<bool>>? processAsync = null)
    {
        Name = name;
        Order = order;
        RetryChannel = retryChannel;
        _result = result;
        FailureMode = failureMode;
        _processAsync = processAsync;
    }

    public string Name { get; }
    public int Order { get; }
    public ConsumerFailureMode FailureMode { get; }
    public string? RetryChannel { get; }

    public int ProcessCallCount { get; private set; }

    public async Task<bool> ProcessAsync(CellCompletedRecord record)
    {
        ProcessCallCount++;

        if (_processAsync is not null)
        {
            return await _processAsync(record);
        }

        return _result;
    }
}

internal sealed class FakeFailedRecordStore : ICloudRetryRecordStore, IMesRetryRecordStore
{
    public sealed record RetryUpdate(long Id, int RetryCount, string ErrorMessage, DateTime NextRetryTime);

    public List<FailedCellRecord> PendingRecords { get; } = new();
    public Dictionary<long, RetryUpdate> Updates { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public int ResetAllAbandonedCallCount { get; private set; }
    public int DeleteExpiredAbandonedCallCount { get; private set; }
    public int SaveCallCount { get; private set; }
    public Exception? SaveException { get; set; }
    public Exception? CloudCountException { get; set; }
    public Exception? MesCountException { get; set; }
    public TimeSpan CloudCountDelay { get; set; }
    public TimeSpan MesCountDelay { get; set; }
    public Queue<Exception> SaveExceptions { get; } = new();
    public DateTime? LastDeleteExpiredOlderThanUtc { get; private set; }
    public int DeleteExpiredAbandonedResult { get; set; }

    Task ICloudRetryRecordStore.SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage)
        => SaveAsync(record, failedTarget, errorMessage, "Cloud");

    Task IMesRetryRecordStore.SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage)
        => SaveAsync(record, failedTarget, errorMessage, "MES");

    Task<List<FailedCellRecord>> ICloudRetryRecordStore.GetPendingAsync(int batchSize)
        => GetPendingAsync("Cloud", batchSize);

    Task<List<FailedCellRecord>> IMesRetryRecordStore.GetPendingAsync(int batchSize)
        => GetPendingAsync("MES", batchSize);

    Task<int> ICloudRetryRecordStore.GetCountAsync()
        => GetCountAsync("Cloud");

    Task<int> IMesRetryRecordStore.GetCountAsync()
        => GetCountAsync("MES");

    Task<int> ICloudRetryRecordStore.GetCountAsync(string processType)
        => GetCountAsync("Cloud", processType);

    Task<int> IMesRetryRecordStore.GetCountAsync(string processType)
        => GetCountAsync("MES", processType);

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
            NextRetryTime = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }

    public Task<List<FailedCellRecord>> GetPendingAsync(string channel, int batchSize = 10)
    {
        var now = DateTime.UtcNow;
        var rows = PendingRecords
            .Where(r => r.Channel == channel && r.NextRetryTime <= now)
            .OrderBy(r => r.Id)
            .Take(batchSize)
            .ToList();

        return Task.FromResult(rows);
    }

    public Task DeleteAsync(long id)
    {
        DeletedIds.Add(id);
        PendingRecords.RemoveAll(x => x.Id == id);
        return Task.CompletedTask;
    }

    public Task UpdateRetryAsync(long id, int retryCount, string errorMessage, DateTime nextRetryTime)
    {
        Updates[id] = new RetryUpdate(id, retryCount, errorMessage, nextRetryTime);
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync() => Task.FromResult(PendingRecords.Count);

    public async Task<int> GetCountAsync(string channel)
    {
        if (TryGetCountException(channel) is { } ex)
        {
            throw ex;
        }

        await MaybeDelayAsync(channel);
        return PendingRecords.Count(x => x.Channel == channel);
    }

    public async Task<int> GetCountAsync(string channel, string processType)
    {
        if (TryGetCountException(channel) is { } ex)
        {
            throw ex;
        }

        await MaybeDelayAsync(channel);
        return PendingRecords.Count(x =>
            x.Channel == channel
            && string.Equals(x.ProcessType, processType, StringComparison.OrdinalIgnoreCase));
    }

    public Task ResetAllAbandonedAsync()
    {
        ResetAllAbandonedCallCount++;
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

    private Task MaybeDelayAsync(string channel)
    {
        var delay = channel switch
        {
            "Cloud" => CloudCountDelay,
            "MES" => MesCountDelay,
            _ => TimeSpan.Zero
        };

        return delay > TimeSpan.Zero
            ? Task.Delay(delay)
            : Task.CompletedTask;
    }
}

internal sealed class FakeCloudFallbackBufferStore : ICloudFallbackBufferStore
{
    public List<CloudFallbackRecord> Records { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public int SaveCallCount { get; private set; }
    public Exception? SaveException { get; set; }

    public Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage)
    {
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
            CreatedAt = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public Task<List<CloudFallbackRecord>> GetPendingAsync(int batchSize = 50)
        => Task.FromResult(Records.OrderBy(x => x.Id).Take(batchSize).ToList());

    public Task DeleteBatchAsync(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        DeletedIds.AddRange(idList);
        Records.RemoveAll(x => idList.Contains(x.Id));
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync() => Task.FromResult(Records.Count);
}

internal sealed class FakeMesFallbackBufferStore : IMesFallbackBufferStore
{
    public List<MesFallbackRecord> Records { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public int SaveCallCount { get; private set; }
    public Exception? SaveException { get; set; }

    public Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage)
    {
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
            CreatedAt = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public Task<List<MesFallbackRecord>> GetPendingAsync(int batchSize = 50)
        => Task.FromResult(Records.OrderBy(x => x.Id).Take(batchSize).ToList());

    public Task DeleteBatchAsync(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        DeletedIds.AddRange(idList);
        Records.RemoveAll(x => idList.Contains(x.Id));
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync() => Task.FromResult(Records.Count);
}

internal sealed class FakeCloudDeadLetterStore : ICloudDeadLetterStore
{
    public List<DeadLetterRecord> Records { get; } = new();
    public Exception? SaveException { get; set; }

    public Task SaveAsync(DeadLetterRecord record)
    {
        if (SaveException is not null)
        {
            throw SaveException;
        }

        Records.Add(record);
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync() => Task.FromResult(Records.Count);
}

internal sealed class FakeMesDeadLetterStore : IMesDeadLetterStore
{
    public List<DeadLetterRecord> Records { get; } = new();
    public Exception? SaveException { get; set; }

    public Task SaveAsync(DeadLetterRecord record)
    {
        if (SaveException is not null)
        {
            throw SaveException;
        }

        Records.Add(record);
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync() => Task.FromResult(Records.Count);
}

internal sealed class FakeCriticalPersistenceFallbackWriter : ICriticalPersistenceFallbackWriter
{
    public sealed record WriteEntry(string Source, string Details, Exception? Exception);

    public List<WriteEntry> Writes { get; } = new();

    public void Write(string source, string details, Exception? exception = null)
        => Writes.Add(new WriteEntry(source, details, exception));
}

internal sealed class FakeIngressOverflowPersistence : IIngressOverflowPersistence
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

internal sealed class FakeDeviceLogBufferStore : IDeviceLogBufferStore
{
    private readonly Dictionary<string, List<long>> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _claimedRecordIds = new();

    public List<DeviceLogRecord> Records { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public List<string> DeletedClaimTokens { get; } = new();
    public List<string> ReleasedClaimTokens { get; } = new();
    public Exception? SaveBatchException { get; set; }
    public Exception? CountException { get; set; }
    public TimeSpan CountDelay { get; set; }

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

        return Task.FromResult<ClaimedDeviceLogBatch?>(new ClaimedDeviceLogBatch
        {
            ClaimToken = claimToken,
            Records = rows.Select(CloneDeviceLogRecord).ToList()
        });
    }

    public Task DeleteClaimedBatchAsync(string claimToken)
    {
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

        if (CountDelay > TimeSpan.Zero)
        {
            await Task.Delay(CountDelay);
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

internal sealed class FakeCloudHttpClient : ICloudHttpClient
{
    private readonly Queue<CloudCallResult> _postResults = new();
    private readonly Queue<CloudCallResult<string>> _postWithResponseResults = new();
    private readonly Queue<CloudCallResult<string>> _getResults = new();

    public int PostCallCount { get; private set; }
    public int GetCallCount { get; private set; }
    public string? LastPostUrl { get; private set; }
    public object? LastPayload { get; private set; }
    public CloudRequestOptions? LastPostOptions { get; private set; }
    public CloudRequestOptions? LastGetOptions { get; private set; }
    public List<string> PostUrls { get; } = new();
    public List<object> PostPayloads { get; } = new();
    public List<string?> PostIdempotencyKeys { get; } = new();
    public List<string?> GetIdempotencyKeys { get; } = new();
    public List<string> GetUrls { get; } = new();

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

    public Task<CloudCallResult> PostAsync(string url, object payload, CloudRequestOptions? options = null)
    {
        PostCallCount++;
        LastPostUrl = url;
        LastPayload = payload;
        LastPostOptions = options;
        PostUrls.Add(url);
        PostPayloads.Add(payload);
        PostIdempotencyKeys.Add(options?.IdempotencyKey);

        if (_postResults.Count > 0)
        {
            return Task.FromResult(_postResults.Dequeue());
        }

        return Task.FromResult(CloudCallResult.Success());
    }

    public Task<CloudCallResult<string>> PostWithResponseAsync(string url, object payload, CloudRequestOptions? options = null)
    {
        PostCallCount++;
        LastPostUrl = url;
        LastPayload = payload;
        LastPostOptions = options;
        PostUrls.Add(url);
        PostPayloads.Add(payload);
        PostIdempotencyKeys.Add(options?.IdempotencyKey);

        if (_postWithResponseResults.Count > 0)
        {
            return Task.FromResult(_postWithResponseResults.Dequeue());
        }

        return Task.FromResult(CloudCallResult<string>.Success(null));
    }

    public Task<CloudCallResult<string>> GetAsync(string url, CloudRequestOptions? options = null)
    {
        GetCallCount++;
        LastGetOptions = options;
        GetUrls.Add(url);
        GetIdempotencyKeys.Add(options?.IdempotencyKey);

        if (_getResults.Count > 0)
        {
            return Task.FromResult(_getResults.Dequeue());
        }

        return Task.FromResult(CloudCallResult<string>.Success(null));
    }
}

internal sealed class FakeCloudApiEndpointProvider : ICloudApiEndpointProvider
{
    private static readonly Uri BaseUri = new("https://cloud.test");

    public string BuildUrl(string relativeOrAbsoluteUrl)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        return new Uri(BaseUri, relativeOrAbsoluteUrl).ToString();
    }

    public string GetClientCode() => "TEST";
    public string GetDeviceInstancePath() => "/api/v1/edge/bootstrap/device-instance";
    public string GetIdentityDeviceLoginPath() => "/api/v1/human/identity/edge-login";
    public string GetDeviceLogPath() => "/api/v1/edge/device-logs";
    public string BuildRecipeByDevicePath(Guid deviceId) => $"/api/v1/edge/recipes/device/{deviceId}";
    public string GetCapacityHourlyPath() => "/api/v1/edge/capacity/hourly";
    public string GetCapacitySummaryPath() => "/api/v1/edge/capacity/summary";
    public string GetCapacitySummaryRangePath() => "/api/v1/edge/capacity/summary/range";
}

internal sealed class FakeDeviceAccessTokenProvider(string? accessToken = null) : IDeviceAccessTokenProvider
{
    public string? AccessToken { get; set; } = accessToken;
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
}

internal sealed class FakeCapacityBufferStore : ICapacityBufferStore
{
    private readonly Dictionary<string, List<BufferHourlySummaryDto>> _claims = new(StringComparer.OrdinalIgnoreCase);

    public List<CapacityRecord> Records { get; } = new();
    public List<BufferSummaryDto> ShiftSummaries { get; } = new();
    public List<BufferHourlySummaryDto> HourlySummaries { get; } = new();
    public List<string> ReleasedClaimTokens { get; } = new();
    public List<(string ClaimToken, string Date, int Hour, int MinuteBucket, string ShiftCode, string PlcName)> DeletedSummaries { get; } = new();
    public int ClearAllCallCount { get; private set; }
    public Exception? CountException { get; set; }
    public TimeSpan CountDelay { get; set; }

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

    public Task<List<BufferSummaryDto>> GetShiftSummaryAsync()
        => Task.FromResult(ShiftSummaries.Select(CloneShiftSummary).ToList());

    public Task<List<BufferHourlySummaryDto>> GetHourlySummaryAsync()
        => Task.FromResult(HourlySummaries.Select(CloneHourlySummary).ToList());

    public Task<ClaimedCapacityBufferBatch?> ClaimHourlySummaryBatchAsync(int batchSize = 200)
    {
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
        ShiftSummaries.Clear();
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

        if (CountDelay > TimeSpan.Zero)
        {
            await Task.Delay(CountDelay);
        }

        return Records.Count;
    }

    private static BufferSummaryDto CloneShiftSummary(BufferSummaryDto source)
        => new()
        {
            Date = source.Date,
            ShiftCode = source.ShiftCode,
            Total = source.Total,
            OkCount = source.OkCount,
            NgCount = source.NgCount
        };

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

internal sealed class FakeProductionContextStore : IProductionContextStore
{
    private readonly Dictionary<string, ProductionContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

    public ProductionContextPersistenceDiagnostics PersistenceDiagnostics { get; set; } = new(0, null);

    public ProductionContext GetOrCreate(string deviceName)
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

internal sealed class FakeCloudBatchConsumer : ICloudBatchConsumer
{
    private readonly Queue<CloudCallResult> _results = new();

    public int ProcessBatchCallCount { get; private set; }
    public List<IReadOnlyList<CellCompletedRecord>> ReceivedBatches { get; } = new();

    public void EnqueueResult(bool result)
        => _results.Enqueue(
            result
                ? CloudCallResult.Success()
                : CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "fake_batch_failure"));

    public void EnqueueResult(CloudCallResult result) => _results.Enqueue(result);

    public Task<CloudCallResult> ProcessBatchAsync(IReadOnlyList<CellCompletedRecord> records)
    {
        ProcessBatchCallCount++;
        ReceivedBatches.Add(records.ToList());

        if (_results.Count > 0)
        {
            return Task.FromResult(_results.Dequeue());
        }

        return Task.FromResult(CloudCallResult.Success());
    }
}

internal sealed class FakeCloudDiagnosticsStore : ICloudUploadDiagnosticsStore
{
    public CloudUploadDiagnosticsSnapshot Snapshot { get; private set; } = new(
        LastAttemptAt: null,
        LastSuccessAt: null,
        LastFailureAt: null,
        LastOutcome: CloudCallOutcome.Success,
        LastReasonCode: "none",
        LastProcessType: null,
        RuntimeState: CloudRetryRuntimeState.Idle,
        IsCapacityBlocked: false,
        BlockedChannel: null,
        BlockedReason: "none",
        LastCapacityBlockAt: null);

    public void RecordResult(string? processType, CloudCallResult result)
    {
        var now = DateTime.Now;
        Snapshot = Snapshot with
        {
            LastAttemptAt = now,
            LastSuccessAt = result.IsSuccess ? now : Snapshot.LastSuccessAt,
            LastFailureAt = result.IsSuccess ? Snapshot.LastFailureAt : now,
            LastOutcome = result.Outcome,
            LastReasonCode = string.IsNullOrWhiteSpace(result.ReasonCode) ? "unknown" : result.ReasonCode,
            LastProcessType = processType
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
            LastCapacityBlockAt = occurredAt ?? DateTime.Now
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

internal sealed class FakeMesRetryDiagnosticsStore : IMesRetryDiagnosticsStore
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
            LastCapacityBlockAt = occurredAt ?? DateTime.Now
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

internal sealed class FakeCloudConsumer : ICloudConsumer
{
    private readonly Queue<CloudCallResult> _results = new();

    public string Name { get; init; } = "Cloud";
    public int Order { get; init; } = 20;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;
    public string? RetryChannel => "Cloud";
    public int ProcessCallCount { get; private set; }
    public List<CellCompletedRecord> ProcessedRecords { get; } = new();

    public void EnqueueResult(bool success)
        => _results.Enqueue(
            success
                ? CloudCallResult.Success()
                : CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "fake_cloud_failure"));

    public void EnqueueResult(CloudCallResult result) => _results.Enqueue(result);

    public async Task<bool> ProcessAsync(CellCompletedRecord record)
        => (await ProcessWithResultAsync(record).ConfigureAwait(false)).IsSuccess;

    public Task<CloudCallResult> ProcessWithResultAsync(CellCompletedRecord record)
    {
        ProcessCallCount++;
        ProcessedRecords.Add(record);

        if (_results.Count > 0)
        {
            return Task.FromResult(_results.Dequeue());
        }

        return Task.FromResult(CloudCallResult.Success());
    }
}

internal sealed class FakeMesConsumer : IMesConsumer
{
    private readonly Queue<bool> _results = new();

    public string Name { get; init; } = "MES";
    public int Order { get; init; } = 30;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;
    public string? RetryChannel => "MES";
    public int ProcessCallCount { get; private set; }
    public List<CellCompletedRecord> ProcessedRecords { get; } = new();

    public void EnqueueResult(bool success) => _results.Enqueue(success);

    public Task<bool> ProcessAsync(CellCompletedRecord record)
    {
        ProcessCallCount++;
        ProcessedRecords.Add(record);

        if (_results.Count > 0)
        {
            return Task.FromResult(_results.Dequeue());
        }

        return Task.FromResult(true);
    }
}

internal sealed class FakeDeviceLogSyncTask : IDeviceLogSyncTask
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

internal sealed class FakeCapacitySyncTask : ICapacitySyncTask
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

internal sealed class FakeMesUploadDiagnosticsStore : IMesUploadDiagnosticsStore
{
    private readonly Dictionary<string, MesChannelDiagnostics> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MesChannelDiagnostics> GetAll()
        => _entries.Values.OrderBy(x => x.ProcessType, StringComparer.OrdinalIgnoreCase).ToArray();

    public MesChannelDiagnostics? Get(string processType)
        => _entries.TryGetValue(processType, out var diagnostics) ? diagnostics : null;

    public void RecordSuccess(string processType)
    {
        var now = DateTime.Now;
        _entries[processType] = new MesChannelDiagnostics(
            processType,
            now,
            now,
            "Success",
            null);
    }

    public void RecordFailure(string processType, string failureReason)
    {
        var now = DateTime.Now;
        var lastSuccessAt = _entries.TryGetValue(processType, out var existing)
            ? existing.LastSuccessAt
            : null;

        _entries[processType] = new MesChannelDiagnostics(
            processType,
            now,
            lastSuccessAt,
            "Failed",
            failureReason);
    }
}

internal sealed class FakeMesUploader : IProcessMesUploader
{
    private readonly Queue<bool> _results = new();

    public FakeMesUploader(string processType, MesUploadMode uploadMode = MesUploadMode.Single)
    {
        ProcessType = processType;
        UploadMode = uploadMode;
    }

    public string ProcessType { get; }

    public MesUploadMode UploadMode { get; }

    public int UploadCallCount { get; private set; }

    public List<IReadOnlyList<CellCompletedRecord>> UploadedBatches { get; } = new();

    public void EnqueueResult(bool result) => _results.Enqueue(result);

    public Task<bool> UploadAsync(
        ProcessMesUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        UploadCallCount++;
        UploadedBatches.Add(records.ToList());

        if (_results.Count > 0)
        {
            return Task.FromResult(_results.Dequeue());
        }

        return Task.FromResult(true);
    }
}

internal sealed class FakeProcessIntegrationRegistry : IProcessIntegrationRegistry
{
    private readonly Dictionary<string, CloudUploaderRegistration> _cloud = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MesUploaderRegistration> _mes = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode)
        => _cloud[processType] = new CloudUploaderRegistration(processType, uploadMode);

    public void RegisterMesUploader(string processType, MesUploadMode uploadMode)
        => _mes[processType] = new MesUploaderRegistration(processType, uploadMode);

    public bool HasCloudUploader(string processType) => _cloud.ContainsKey(processType);

    public bool HasMesUploader(string processType) => _mes.ContainsKey(processType);

    public bool TryGetCloudUploader(string processType, out CloudUploaderRegistration registration)
        => _cloud.TryGetValue(processType, out registration!);

    public bool TryGetMesUploader(string processType, out MesUploaderRegistration registration)
        => _mes.TryGetValue(processType, out registration!);

    public IReadOnlyDictionary<string, CloudUploaderRegistration> GetCloudUploaders() => _cloud;

    public IReadOnlyDictionary<string, MesUploaderRegistration> GetMesUploaders() => _mes;
}
