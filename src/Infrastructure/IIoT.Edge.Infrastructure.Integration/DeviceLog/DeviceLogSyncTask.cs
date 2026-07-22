using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Module.Contracts.DataPipeline.DeviceLog;

namespace IIoT.Edge.Infrastructure.Integration.DeviceLog;

public class DeviceLogSyncTask : IDeviceLogSyncTask
{
    private readonly ICloudHttpClient _cloudHttp;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly IDeviceService _deviceService;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly IDeviceLogBufferStore _bufferStore;
    private readonly ILogService _logger;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly object _queueLock = new();
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private Queue<LogItem> _queue = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _isRunning;
    private bool _isSubscribed;
    private const int RetryBatchSize = 100;
    private const int RetryMaxBatchesPerRound = 3;

    public DeviceLogSyncTask(
        ICloudHttpClient cloudHttp,
        ICloudApiEndpointProvider endpointProvider,
        IDeviceService deviceService,
        ILocalSystemRuntimeConfigService runtimeConfig,
        IDeviceLogBufferStore bufferStore,
        ILogService logger,
        ICloudUploadDiagnosticsStore diagnosticsStore)
    {
        _cloudHttp = cloudHttp;
        _endpointProvider = endpointProvider;
        _deviceService = deviceService;
        _runtimeConfig = runtimeConfig;
        _bufferStore = bufferStore;
        _logger = logger;
        _diagnosticsStore = diagnosticsStore;
    }

    public Task StartAsync(CancellationToken ct)
    {
        lock (_lifecycleLock)
        {
            if (_isRunning)
            {
                return Task.CompletedTask;
            }

            if (!_isSubscribed)
            {
                _logger.EntryAdded += OnLogEntryAdded;
                _isSubscribed = true;
            }

            _isRunning = true;

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = linkedCts;
            _loopTask = Task.Run(() => SyncLoopAsync(linkedCts.Token), CancellationToken.None);
        }

        _logger.Info($"[设备日志同步] 已启动，间隔：{(int)_runtimeConfig.Current.CloudSyncInterval.TotalSeconds}s");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? localCts;
        Task? localLoopTask;

        lock (_lifecycleLock)
        {
            if (!_isRunning && !_isSubscribed)
            {
                return;
            }

            _isRunning = false;
            localCts = _cts;
            localLoopTask = _loopTask;
            _cts = null;
            _loopTask = null;

            if (_isSubscribed)
            {
                _logger.EntryAdded -= OnLogEntryAdded;
                _isSubscribed = false;
            }
        }

        if (localCts is not null)
        {
            await localCts.CancelAsync();
            if (localLoopTask is not null)
            {
                try
                {
                    await localLoopTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            localCts.Dispose();
        }

        try
        {
            if (_runtimeConfig.Current.SystemCloudEnabled)
            {
                await FlushQueueToBufferAsync().ConfigureAwait(false);
            }
            else
            {
                DrainQueue();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[设备日志同步] 停止前刷新失败：{ex.Message}");
        }

        _logger.Info("[设备日志同步] 已停止。");
    }

    private void OnLogEntryAdded(LogEntry entry)
    {
        if (!_runtimeConfig.Current.SystemCloudEnabled)
        {
            return;
        }

        lock (_queueLock)
        {
            _queue.Enqueue(new LogItem
            {
                Level = entry.Level,
                Message = entry.Message,
                LogTime = entry.Time
            });
        }
    }

    private async Task SyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_runtimeConfig.Current.CloudSyncInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await ExecuteOnceAsync(ct);
        }
    }

    private async Task ExecuteOnceAsync(CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_runtimeConfig.Current.SystemCloudEnabled)
            {
                DrainQueue();
                return;
            }

            await FlushQueueToBufferAsync().ConfigureAwait(false);

            if (!_deviceService.CanUploadToCloud)
            {
                return;
            }

            var device = _deviceService.CurrentDevice;
            if (device is null)
            {
                return;
            }

            await RetryBufferedLogsCoreAsync(device, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[设备日志同步] 执行失败：{ex.Message}");
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<bool> RetryBufferedLogsCoreAsync(
        DeviceSession device,
        CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < RetryMaxBatchesPerRound; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claimedBatch = await _bufferStore.ClaimPendingBatchAsync(RetryBatchSize).ConfigureAwait(false);
            if (claimedBatch is null || claimedBatch.Records.Count == 0)
            {
                return true;
            }

            CloudCallResult? result = null;
            var claimReleased = false;
            var claimDeleted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await PostLogsAsync(
                        device.DeviceId,
                        claimedBatch.Records,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _bufferStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                    claimReleased = true;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (result.Outcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry)
                    {
                        _logger.Warn($"[设备日志同步] 补传已暂停，等待云端恢复。结果：{result.Outcome}，原因：{result.ReasonCode}");
                    }

                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await _bufferStore.DeleteClaimedBatchAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                claimDeleted = true;
                cancellationToken.ThrowIfCancellationRequested();

                if (claimedBatch.Records.Count < RetryBatchSize)
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!claimReleased && !claimDeleted)
                {
                    await TryReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                }
                throw;
            }
            catch (Exception ex)
            {
                if (!claimReleased && !claimDeleted)
                {
                    await TryReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                }

                _logger.Error($"[设备日志同步] 缓冲日志补传失败：{ex.Message}");
                return false;
            }
        }

        return true;
    }

    private async Task TryReleaseClaimAsync(string claimToken)
    {
        try
        {
            await _bufferStore.ReleaseClaimAsync(claimToken).ConfigureAwait(false);
        }
        catch (Exception releaseEx)
        {
            _logger.Error(
                $"[设备日志同步] 释放设备日志补传领取标记 {claimToken} 失败：{releaseEx.Message}");
        }
    }

    private async Task<CloudCallResult> PostLogsAsync(
        Guid deviceId,
        IReadOnlyCollection<DeviceLogRecord> batch,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            deviceId,
            logs = batch.Select(l => new
            {
                level = l.Level,
                message = l.Message,
                logTime = l.LogTime
            }).ToArray()
        };

        var result = await _cloudHttp.PostAsync(
                _endpointProvider.GetDeviceLogPath(),
                payload,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _diagnosticsStore.RecordResult("DeviceLog", result);
        return result;
    }

    private async Task SaveToBufferAsync(List<LogItem> batch)
    {
        var createdAt = DateTime.UtcNow.ToString("O");
        var records = batch.Select(l => new DeviceLogRecord
        {
            Level = l.Level,
            Message = l.Message,
            LogTime = l.LogTime.ToString("O"),
            CreatedAt = createdAt
        });

        await _bufferStore.SaveBatchAsync(records).ConfigureAwait(false);
    }

    private async Task FlushQueueToBufferAsync()
    {
        var remaining = DrainQueue();
        if (remaining.Count == 0)
        {
            return;
        }

        try
        {
            await SaveToBufferAsync(remaining).ConfigureAwait(false);
        }
        catch
        {
            RequeueToFront(remaining);
            throw;
        }
    }

    private List<LogItem> DrainQueue()
    {
        lock (_queueLock)
        {
            if (_queue.Count == 0)
            {
                return [];
            }

            var list = _queue.ToList();
            _queue.Clear();
            return list;
        }
    }

    private void RequeueToFront(List<LogItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        lock (_queueLock)
        {
            _queue = new Queue<LogItem>(items.Concat(_queue));
        }
    }

    public async Task<bool> RetryBufferAsync()
    {
        await _syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_runtimeConfig.Current.SystemCloudEnabled)
            {
                return true;
            }

            if (!_deviceService.CanUploadToCloud)
            {
                return false;
            }

            var device = _deviceService.CurrentDevice;
            if (device is null)
            {
                return false;
            }

            return await RetryBufferedLogsCoreAsync(device).ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private class LogItem
    {
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime LogTime { get; set; }
    }
}
