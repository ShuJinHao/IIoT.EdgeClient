using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;

namespace IIoT.Edge.Infrastructure.Integration.Capacity;

public class CapacitySyncTask : ICapacitySyncTask
{
    private readonly ICloudHttpClient _cloudHttp;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly IDeviceService _deviceService;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly IProductionContextStore _contextStore;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService _logger;
    private readonly ShiftConfig _shiftConfig;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _isRunning;

    public CapacitySyncTask(
        ICloudHttpClient cloudHttp,
        ICloudApiEndpointProvider endpointProvider,
        IDeviceService deviceService,
        ILocalSystemRuntimeConfigService runtimeConfig,
        IProductionContextStore contextStore,
        ICapacityBufferStore bufferStore,
        ILogService logger,
        ShiftConfig shiftConfig,
        ICloudUploadDiagnosticsStore diagnosticsStore)
    {
        _cloudHttp = cloudHttp;
        _endpointProvider = endpointProvider;
        _deviceService = deviceService;
        _runtimeConfig = runtimeConfig;
        _contextStore = contextStore;
        _bufferStore = bufferStore;
        _logger = logger;
        _shiftConfig = shiftConfig;
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

            _isRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loopTask = Task.Run(() => SyncLoopAsync(_cts.Token), CancellationToken.None);
        }

        _logger.Info($"[CapacitySync] 已启动，间隔：{(int)_runtimeConfig.Current.CloudSyncInterval.TotalSeconds}s");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? localCts;
        Task? localLoopTask;

        lock (_lifecycleLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            localCts = _cts;
            localLoopTask = _loopTask;
            _cts = null;
            _loopTask = null;
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

        _logger.Info("[CapacitySync] 已停止。");
    }

    private async Task ExecuteOnceAsync()
    {
        await _syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!TryGetCloudDeviceId(out var deviceId))
            {
                return;
            }

            try
            {
                await SyncAllDevicesAsync(deviceId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"[CapacitySync] 同步失败：{ex.Message}");
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task SyncAllDevicesAsync(Guid cloudDeviceId)
    {
        var contexts = _contextStore.GetAll();
        foreach (var ctx in contexts)
        {
            var capacity = ctx.TodayCapacity.CreateSnapshot();
            if (string.IsNullOrWhiteSpace(capacity.Date) || capacity.TotalAll == 0)
            {
                continue;
            }

            foreach (var slot in capacity.HalfHourly.Where(h => h.Total > 0).OrderBy(h => h.SlotIndex))
            {
                var shiftCode = GetShiftCodeByTime(slot.StartHour, slot.StartMinute);
                await PostHalfHourCapacityAsync(
                    cloudDeviceId,
                    capacity.Date,
                    slot.StartHour,
                    slot.StartMinute,
                    shiftCode,
                    slot.Total,
                    slot.OkCount,
                    slot.NgCount,
                    ctx.DeviceName).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> PostHalfHourCapacityAsync(
        Guid deviceId,
        string date,
        int hour,
        int minute,
        string shiftCode,
        int totalCount,
        int okCount,
        int ngCount,
        string plcName)
    {
        var result = await PostCapacityAsync(
            deviceId,
            date,
            hour,
            minute,
            shiftCode,
            totalCount,
            okCount,
            ngCount,
            plcName).ConfigureAwait(false);
        var slotLabel = FormatCapacitySlot(date, hour, minute, shiftCode);
        if (result.IsSuccess)
        {
            _logger.Info(
                $"[CapacitySync] [{plcName}] {slotLabel} 已同步。总数：{totalCount}，OK：{okCount}，NG：{ngCount}");
        }
        else if (IsUploadPaused(result))
        {
            _logger.Warn(
                $"[CapacitySync] [{plcName}] {slotLabel} 等待云端恢复，原因：{result.ReasonCode}");
        }
        else
        {
            _logger.Warn($"[CapacitySync] [{plcName}] {slotLabel} 同步失败。");
        }

        return result.IsSuccess;
    }

    public async Task<bool> RetryBufferAsync()
    {
        await _syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!TryGetCloudDeviceId(out var deviceId))
            {
                return false;
            }

            while (true)
            {
                var claimedBatch = await _bufferStore.ClaimHourlySummaryBatchAsync().ConfigureAwait(false);
                if (claimedBatch is null || claimedBatch.Summaries.Count == 0)
                {
                    return true;
                }

                var claimReleased = false;
                try
                {
                    foreach (var summary in claimedBatch.Summaries)
                    {
                        var result = await PostCapacityAsync(
                            deviceId,
                            summary.Date,
                            summary.Hour,
                            summary.MinuteBucket,
                            summary.ShiftCode,
                            summary.Total,
                            summary.OkCount,
                            summary.NgCount,
                            summary.PlcName).ConfigureAwait(false);
                        if (!result.IsSuccess)
                        {
                            await _bufferStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                            claimReleased = true;
                            var slotLabel = FormatCapacitySlot(
                                summary.Date,
                                summary.Hour,
                                summary.MinuteBucket,
                                summary.ShiftCode);
                            if (IsUploadPaused(result))
                            {
                                _logger.Warn(
                                    $"[Retry-Cloud] 产能补传已暂停，等待云端恢复：{slotLabel}（{result.ReasonCode}）");
                            }
                            else
                            {
                                _logger.Warn(
                                    $"[Retry-Cloud] 产能补传失败：{slotLabel}");
                            }
                            return false;
                        }

                        await _bufferStore.DeleteClaimedSummaryAsync(
                            claimedBatch.ClaimToken,
                            summary.Date,
                            summary.Hour,
                            summary.MinuteBucket,
                            summary.ShiftCode,
                            summary.PlcName).ConfigureAwait(false);
                    }

                    _logger.Info(
                        $"[Retry-Cloud] 产能补传批次 {claimedBatch.ClaimToken} 已完成，行数：{claimedBatch.Summaries.Count}");
                }
                catch (Exception ex)
                {
                    if (!claimReleased)
                    {
                        try
                        {
                            await _bufferStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                        }
                        catch (Exception releaseEx)
                        {
                            _logger.Error(
                                $"[Retry-Cloud] 释放产能补传领取标记 {claimedBatch.ClaimToken} 失败：{releaseEx.Message}");
                        }
                    }

                    _logger.Error($"[Retry-Cloud] 产能补传异常：{ex.Message}");
                    return false;
                }
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private bool TryGetCloudDeviceId(out Guid deviceId)
    {
        deviceId = default;
        if (!_deviceService.CanUploadToCloud)
        {
            return false;
        }

        var device = _deviceService.CurrentDevice;
        if (device is null)
        {
            return false;
        }

        deviceId = device.DeviceId;
        return true;
    }

    private async Task<CloudCallResult> PostCapacityAsync(
        Guid deviceId,
        string date,
        int hour,
        int minute,
        string shiftCode,
        int totalCount,
        int okCount,
        int ngCount,
        string plcName)
    {
        var payload = CreatePayload(deviceId, date, hour, minute, shiftCode, totalCount, okCount, ngCount, plcName);
        var result = await _cloudHttp.PostAsync(_endpointProvider.GetCapacityHourlyPath(), payload).ConfigureAwait(false);
        _diagnosticsStore.RecordResult("Capacity", result);
        return result;
    }

    private object CreatePayload(
        Guid deviceId,
        string date,
        int hour,
        int minute,
        string shiftCode,
        int totalCount,
        int okCount,
        int ngCount,
        string plcName)
    {
        var endMinute = minute == 30 ? 0 : 30;
        var endHour = minute == 30 ? (hour + 1) % 24 : hour;

        return new
        {
            deviceId,
            date,
            hour,
            minute,
            timeLabel = $"{hour:D2}:{minute:D2}-{endHour:D2}:{endMinute:D2}",
            shiftCode,
            totalCount,
            okCount,
            ngCount,
            plcName
        };
    }

    private string GetShiftCodeByTime(int hour, int minute)
    {
        var time = new TimeSpan(hour, minute, 0);
        var isDay = time >= _shiftConfig.DayStartTime && time < _shiftConfig.DayEndTime;
        return isDay ? "D" : "N";
    }

    private static bool IsUploadPaused(CloudCallResult result)
        => result.Outcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry;

    private static string FormatCapacitySlot(string date, int hour, int minute, string shiftCode)
        => $"{date} {hour:D2}:{minute:D2}/{shiftCode}";
}
