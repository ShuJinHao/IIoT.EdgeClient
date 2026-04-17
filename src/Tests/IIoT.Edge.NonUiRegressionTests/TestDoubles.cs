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

internal sealed class FakeDeviceService : IDeviceService
{
    public DeviceSession? CurrentDevice { get; set; }
    public NetworkState CurrentState { get; set; } = NetworkState.Offline;
    public bool HasDeviceId { get; set; }

    public event Action<NetworkState>? NetworkStateChanged;
    public event Action<DeviceSession?>? DeviceIdentified;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public void SetOnline(DeviceSession session)
    {
        CurrentDevice = session;
        CurrentState = NetworkState.Online;
        HasDeviceId = true;
        DeviceIdentified?.Invoke(session);
        NetworkStateChanged?.Invoke(CurrentState);
    }

    public void SetOffline()
    {
        CurrentState = NetworkState.Offline;
        HasDeviceId = false;
        NetworkStateChanged?.Invoke(CurrentState);
    }
}

internal sealed class FakeDataPipelineService : IDataPipelineService
{
    private readonly Queue<CellCompletedRecord> _queue = new();

    public int PendingCount => _queue.Count;

    public void Enqueue(CellCompletedRecord record) => _queue.Enqueue(record);

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

internal sealed class FakeFailedRecordStore : IFailedRecordStore
{
    public sealed record RetryUpdate(long Id, int RetryCount, string ErrorMessage, DateTime NextRetryTime);

    public List<FailedCellRecord> PendingRecords { get; } = new();
    public Dictionary<long, RetryUpdate> Updates { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public int ResetAllAbandonedCallCount { get; private set; }
    public int SaveCallCount { get; private set; }
    public Exception? SaveException { get; set; }

    public Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage, string channel)
    {
        SaveCallCount++;

        if (SaveException is not null)
        {
            throw SaveException;
        }

        PendingRecords.Add(new FailedCellRecord
        {
            Id = PendingRecords.Count == 0 ? 1 : PendingRecords.Max(x => x.Id) + 1,
            Channel = channel,
            FailedTarget = failedTarget,
            ErrorMessage = errorMessage,
            ProcessType = record.CellData.ProcessType,
            CellDataJson = "{}",
            NextRetryTime = DateTime.Now
        });
        return Task.CompletedTask;
    }

    public Task<List<FailedCellRecord>> GetPendingAsync(string channel, int batchSize = 10)
    {
        var now = DateTime.Now;
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

    public Task<int> GetCountAsync(string channel)
        => Task.FromResult(PendingRecords.Count(x => x.Channel == channel));

    public Task<int> GetCountAsync(string channel, string processType)
        => Task.FromResult(PendingRecords.Count(x =>
            x.Channel == channel
            && string.Equals(x.ProcessType, processType, StringComparison.OrdinalIgnoreCase)));

    public Task ResetAllAbandonedAsync()
    {
        ResetAllAbandonedCallCount++;
        return Task.CompletedTask;
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
            CreatedAt = DateTime.Now
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
            CreatedAt = DateTime.Now
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

internal sealed class FakeDeviceLogBufferStore : IDeviceLogBufferStore
{
    private readonly Dictionary<string, List<long>> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _claimedRecordIds = new();

    public List<DeviceLogRecord> Records { get; } = new();
    public List<long> DeletedIds { get; } = new();
    public List<string> DeletedClaimTokens { get; } = new();
    public List<string> ReleasedClaimTokens { get; } = new();
    public Exception? SaveBatchException { get; set; }

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

    public Task<int> GetCountAsync() => Task.FromResult(Records.Count);

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
    private readonly Queue<bool> _postResults = new();

    public int PostCallCount { get; private set; }
    public string? LastPostUrl { get; private set; }
    public object? LastPayload { get; private set; }
    public List<string> PostUrls { get; } = new();
    public List<object> PostPayloads { get; } = new();

    public void EnqueuePostResult(bool result) => _postResults.Enqueue(result);

    public Task<bool> PostAsync(string url, object payload)
    {
        PostCallCount++;
        LastPostUrl = url;
        LastPayload = payload;
        PostUrls.Add(url);
        PostPayloads.Add(payload);

        if (_postResults.Count > 0)
        {
            return Task.FromResult(_postResults.Dequeue());
        }

        return Task.FromResult(true);
    }

    public Task<string?> PostWithResponseAsync(string url, object payload)
        => Task.FromResult<string?>(null);

    public Task<string?> GetAsync(string url)
        => Task.FromResult<string?>(null);
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
    public string GetPassStationInjectionBatchPath() => "/api/v1/edge/pass-stations/injection/batch";
    public string GetPassStationStackingPath() => "/api/v1/edge/pass-stations/stacking";
    public string GetDeviceLogPath() => "/api/v1/edge/device-logs";
    public string BuildRecipeByDevicePath(Guid deviceId) => $"/api/v1/edge/recipes/device/{deviceId}";
    public string GetCapacityHourlyPath() => "/api/v1/edge/capacity/hourly";
    public string GetCapacitySummaryPath() => "/api/v1/edge/capacity/summary";
    public string GetCapacitySummaryRangePath() => "/api/v1/edge/capacity/summary/range";
}

internal sealed class FakeCloudAccessTokenProvider(string? accessToken = null) : ICloudAccessTokenProvider
{
    public string? AccessToken { get; set; } = accessToken;
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

    public Task<int> GetCountAsync()
        => Task.FromResult(Records.Count);

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
    private readonly Queue<bool> _results = new();

    public int ProcessBatchCallCount { get; private set; }
    public List<IReadOnlyList<CellCompletedRecord>> ReceivedBatches { get; } = new();

    public void EnqueueResult(bool result) => _results.Enqueue(result);

    public Task<bool> ProcessBatchAsync(IReadOnlyList<CellCompletedRecord> records)
    {
        ProcessBatchCallCount++;
        ReceivedBatches.Add(records.ToList());

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
