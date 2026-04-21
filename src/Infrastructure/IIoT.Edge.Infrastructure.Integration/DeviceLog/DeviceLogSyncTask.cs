using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.SharedKernel.DataPipeline.DeviceLog;

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

        _logger.Info($"[DeviceLogSync] Started. Interval: {(int)_runtimeConfig.Current.CloudSyncInterval.TotalSeconds}s");
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
            await FlushQueueToBufferAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[DeviceLogSync] Flush on stop failed: {ex.Message}");
        }

        _logger.Info("[DeviceLogSync] Stopped.");
    }

    private void OnLogEntryAdded(LogEntry entry)
    {
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

            await ExecuteOnceAsync();
        }
    }

    private async Task ExecuteOnceAsync()
    {
        await _syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
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

            await RetryBufferedLogsCoreAsync(device).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[DeviceLogSync] Execute failed: {ex.Message}");
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<bool> RetryBufferedLogsCoreAsync(DeviceSession device)
    {
        for (var i = 0; i < RetryMaxBatchesPerRound; i++)
        {
            var claimedBatch = await _bufferStore.ClaimPendingBatchAsync(RetryBatchSize).ConfigureAwait(false);
            if (claimedBatch is null || claimedBatch.Records.Count == 0)
            {
                return true;
            }

            CloudCallResult? result = null;
            try
            {
                result = await PostLogsAsync(device.DeviceId, claimedBatch.Records).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    await _bufferStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                    if (result.Outcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry)
                    {
                        _logger.Warn($"[DeviceLogSync] Retry paused waiting for cloud recovery. Outcome:{result.Outcome}, Reason:{result.ReasonCode}");
                    }

                    return false;
                }

                await _bufferStore.DeleteClaimedBatchAsync(claimedBatch.ClaimToken).ConfigureAwait(false);

                if (claimedBatch.Records.Count < RetryBatchSize)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (result is null || !result.IsSuccess)
                {
                    try
                    {
                        await _bufferStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                    }
                    catch (Exception releaseEx)
                    {
                        _logger.Error(
                            $"[DeviceLogSync] Failed to release device log claim {claimedBatch.ClaimToken}: {releaseEx.Message}");
                    }
                }

                _logger.Error($"[DeviceLogSync] Retry buffered logs failed: {ex.Message}");
                return false;
            }
        }

        return true;
    }

    private async Task<CloudCallResult> PostLogsAsync(Guid deviceId, IReadOnlyCollection<DeviceLogRecord> batch)
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

        var result = await _cloudHttp.PostAsync(_endpointProvider.GetDeviceLogPath(), payload).ConfigureAwait(false);
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
