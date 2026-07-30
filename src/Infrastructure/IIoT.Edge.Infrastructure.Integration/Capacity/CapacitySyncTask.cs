using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using IIoT.Edge.Application.Common.Identity;

namespace IIoT.Edge.Infrastructure.Integration.Capacity;

public class CapacitySyncTask : ICapacitySyncTask
{
    private const int RetryBatchSize = 200;
    private const int RetryMaxBatchesPerRound = 3;

    private readonly ICloudHttpClient _cloudHttp;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly IDeviceService _deviceService;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly IProductionContextStore _contextStore;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService _logger;
    private readonly ShiftConfig _shiftConfig;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly IPlcIdentityAliasRegistry _identityAliasRegistry;
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
        ICloudUploadDiagnosticsStore diagnosticsStore,
        IPlcIdentityAliasRegistry? identityAliasRegistry = null)
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
        _identityAliasRegistry =
            identityAliasRegistry ?? new InMemoryPlcIdentityAliasRegistry();
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

        _logger.Info($"[产能同步] 已启动，间隔：{(int)_runtimeConfig.Current.CloudSyncInterval.TotalSeconds}s");
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

            await ExecuteOnceAsync(ct);
        }

        _logger.Info("[产能同步] 已停止。");
    }

    private async Task ExecuteOnceAsync(CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetCloudDeviceId(out var deviceId))
            {
                return;
            }

            try
            {
                await SyncAllDevicesAsync(deviceId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"[产能同步] 同步失败：{ex.Message}");
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task SyncAllDevicesAsync(Guid cloudDeviceId, CancellationToken cancellationToken)
    {
        var contexts = _contextStore.GetAll();
        foreach (var ctx in contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(ctx.PlcCode))
            {
                _logger.Error(
                    $"[PlcCode=未解析][TaskKey=Capacity.Hourly][SignalKey=不适用] "
                    + $"产能上下文“{ctx.DeviceName}”缺少稳定身份，已保留本地数据并停止上传。");
                continue;
            }

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
                    ctx.PlcCode,
                    ctx.DeviceName,
                    cancellationToken).ConfigureAwait(false);
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
        string plcCode,
        string deviceName,
        CancellationToken cancellationToken)
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
            plcCode,
            deviceName,
            cancellationToken).ConfigureAwait(false);
        var slotLabel = FormatCapacitySlot(date, hour, minute, shiftCode);
        if (result.IsSuccess)
        {
            _logger.Info(
                $"[PlcCode={plcCode}][产能同步] [{deviceName}] {slotLabel} 已同步。总数：{totalCount}，OK：{okCount}，NG：{ngCount}");
        }
        else if (IsUploadPaused(result))
        {
            _logger.Warn(
                $"[PlcCode={plcCode}][产能同步] [{deviceName}] {slotLabel} 等待云端恢复，原因：{result.ReasonCode}");
        }
        else
        {
            _logger.Warn($"[PlcCode={plcCode}][产能同步] [{deviceName}] {slotLabel} 同步失败。");
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

            for (var batchIndex = 0; batchIndex < RetryMaxBatchesPerRound; batchIndex++)
            {
                var claimedBatch = await _bufferStore.ClaimHourlySummaryBatchAsync(RetryBatchSize).ConfigureAwait(false);
                if (claimedBatch is null || claimedBatch.Summaries.Count == 0)
                {
                    return true;
                }

                var claimReleased = false;
                try
                {
                    foreach (var summary in claimedBatch.Summaries)
                    {
                        if (!TryResolveBufferedPlcIdentity(
                                summary.PlcName,
                                out var plcCode,
                                out var deviceName,
                                out var identityDiagnostic))
                        {
                            await _bufferStore.ReleaseClaimAsync(claimedBatch.ClaimToken).ConfigureAwait(false);
                            claimReleased = true;
                            _logger.Error(
                                $"[PlcCode=未解析][TaskKey=Capacity.Hourly][SignalKey=不适用] "
                                + $"产能补传记录身份无法唯一解析，原始 PlcName={summary.PlcName}，"
                                + $"诊断={identityDiagnostic}；原记录已保留，未上传、移动或删除。");
                            return false;
                        }

                        var result = await PostCapacityAsync(
                            deviceId,
                            summary.Date,
                            summary.Hour,
                            summary.MinuteBucket,
                            summary.ShiftCode,
                            summary.Total,
                            summary.OkCount,
                            summary.NgCount,
                            plcCode,
                            deviceName).ConfigureAwait(false);
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
                                    $"[云端补传] 产能补传已暂停，等待云端恢复：{slotLabel}（{result.ReasonCode}）");
                            }
                            else
                            {
                                _logger.Warn(
                                    $"[云端补传] 产能补传失败：{slotLabel}");
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
                        $"[云端补传] 产能补传批次 {claimedBatch.ClaimToken} 已完成，行数：{claimedBatch.Summaries.Count}");
                    if (claimedBatch.Summaries.Count < RetryBatchSize)
                    {
                        return true;
                    }
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
                                $"[云端补传] 释放产能补传领取标记 {claimedBatch.ClaimToken} 失败：{releaseEx.Message}");
                        }
                    }

                    _logger.Error($"[云端补传] 产能补传异常：{ex.Message}");
                    return false;
                }
            }

            _logger.Info(
                $"[云端补传] 产能补传本轮已处理 {RetryMaxBatchesPerRound} 批，剩余数据等待下一轮。");
            return true;
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

    private bool TryResolveBufferedPlcIdentity(
        string persistedIdentity,
        out string plcCode,
        out string? deviceName,
        out string diagnostic)
    {
        plcCode = string.Empty;
        deviceName = null;
        diagnostic = "capacity_buffer_plc_identity_unresolved";
        var normalizedIdentity = persistedIdentity?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedIdentity))
        {
            return false;
        }

        var matches = _contextStore.GetAll()
            .Where(context => !string.IsNullOrWhiteSpace(context.PlcCode))
            .Where(context =>
                string.Equals(
                    context.PlcCode,
                    normalizedIdentity,
                    StringComparison.OrdinalIgnoreCase)
                || _identityAliasRegistry
                    .GetVerifiedAliases(context.PlcCode)
                    .Contains(normalizedIdentity, StringComparer.OrdinalIgnoreCase))
            .GroupBy(static context => context.PlcCode, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select(static group => group.First())
            .ToArray();
        if (matches.Length != 1)
        {
            diagnostic = matches.Length == 0
                ? "capacity_buffer_plc_identity_unresolved"
                : "capacity_buffer_plc_identity_ambiguous";
            return false;
        }

        plcCode = matches[0].PlcCode.Trim();
        deviceName = matches[0].DeviceName;
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
        string plcCode,
        string? deviceName,
        CancellationToken cancellationToken = default)
    {
        var payload = CreatePayload(
            deviceId,
            date,
            hour,
            minute,
            shiftCode,
            totalCount,
            okCount,
            ngCount,
            plcCode);
        var result = await _cloudHttp.PostAsync(
                _endpointProvider.GetCapacityHourlyPath(),
                payload,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _diagnosticsStore.RecordResult(
            "Capacity",
            result,
            new CloudUploadDiagnosticsContext(
                DeviceName: deviceName,
                ModuleId: "Capacity",
                TaskKey: "Capacity.Hourly",
                Scenario: "产能上传")
            {
                PlcCode = plcCode
            });
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
        string plcCode)
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
            plcName = plcCode
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
