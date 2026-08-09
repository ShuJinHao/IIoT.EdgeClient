using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Application.Common.Plugins;

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
    private readonly IDevicePluginConfigurationSnapshotAccessor? _pluginConfiguration;
    private readonly IReadOnlyList<IProductionContextFactory> _contextFactories;
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _isRunning;
    private long _retryAfterRecordId;

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
        IPlcIdentityAliasRegistry? identityAliasRegistry = null,
        IDevicePluginConfigurationSnapshotAccessor? pluginConfiguration = null,
        IEnumerable<IProductionContextFactory>? contextFactories = null)
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
        _pluginConfiguration = pluginConfiguration;
        _contextFactories = contextFactories?.ToArray() ?? [];
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

            var processType = ResolveProcessType(ctx);
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
                    processType,
                    legacyPlcIdentity: ctx.PlcCode,
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
        string? processType,
        string legacyPlcIdentity,
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
            processType,
            legacyPlcIdentity,
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

            IReadOnlyList<ConfiguredPlcIdentity> configuredPlcs;
            try
            {
                configuredPlcs = await GetConfiguredPlcIdentitiesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"[PlcCode=未解析][TaskKey=Capacity.Hourly][SignalKey=不适用] "
                    + $"读取权威 PLC 身份配置失败，产能补传保持原记录且未领取数据：{ex.Message}");
                return false;
            }

            if (_bufferStore is not ICapacityBufferCursorStore cursorStore)
            {
                _logger.Error(
                    "[PlcCode=未解析][TaskKey=Capacity.Hourly][SignalKey=不适用] "
                    + "产能补传存储未提供有界游标契约，原记录保持不变。");
                return false;
            }

            for (var batchIndex = 0; batchIndex < RetryMaxBatchesPerRound; batchIndex++)
            {
                var claimedBatch = await cursorStore
                    .ClaimHourlySummaryBatchAfterAsync(_retryAfterRecordId, RetryBatchSize)
                    .ConfigureAwait(false);
                if (claimedBatch is null || claimedBatch.Summaries.Count == 0)
                {
                    _retryAfterRecordId = 0;
                    return true;
                }

                var claimReleased = false;
                var legacyV1InBatch = 0;
                try
                {
                    if (claimedBatch.ClaimedRecordCount <= 0
                        || claimedBatch.LastRecordId <= _retryAfterRecordId)
                    {
                        throw new InvalidDataException(
                            "产能补传游标批次未向前推进。");
                    }

                    foreach (var summary in claimedBatch.Summaries)
                    {
                        if (!TryResolveBufferedPlcIdentity(
                                summary.PlcName,
                                configuredPlcs,
                                out var plcCode,
                                out var deviceName,
                                out var processType,
                                out var identityDiagnostic))
                        {
                            plcCode = summary.PlcName.Trim();
                            deviceName = null;
                            processType = null;
                            legacyV1InBatch++;
                            _logger.Warn(
                                $"[PlcCode=未解析][TaskKey=Capacity.Hourly][SignalKey=不适用] "
                                + $"产能补传记录无法确认真实 PLC 名称或工序，"
                                + $"将按 v1 保留原始身份上传；原始 PlcName={summary.PlcName}，"
                                + $"诊断={identityDiagnostic}。");
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
                                deviceName,
                                processType,
                            legacyPlcIdentity: summary.PlcName).ConfigureAwait(false);
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
                        $"[云端补传] 产能补传批次 {claimedBatch.ClaimToken} 已完成，"
                        + $"汇总数：{claimedBatch.Summaries.Count}，原始行数：{claimedBatch.ClaimedRecordCount}，"
                        + $"v1 兼容上传：{legacyV1InBatch}");
                    _retryAfterRecordId = claimedBatch.LastRecordId;
                    if (claimedBatch.ClaimedRecordCount < RetryBatchSize)
                    {
                        _retryAfterRecordId = 0;
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
                $"[云端补传] 产能补传本轮已处理 {RetryMaxBatchesPerRound} 批，"
                + $"下轮从 RecordId>{_retryAfterRecordId} 继续。");
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
        IReadOnlyList<ConfiguredPlcIdentity> configuredPlcs,
        out string plcCode,
        out string? deviceName,
        out string? processType,
        out string diagnostic)
    {
        plcCode = string.Empty;
        deviceName = null;
        processType = null;
        diagnostic = "capacity_buffer_plc_identity_unresolved";
        var normalizedIdentity = persistedIdentity?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedIdentity))
        {
            return false;
        }

        var exactCodeMatches = configuredPlcs
            .Where(identity => string.Equals(
                identity.PlcCode,
                normalizedIdentity,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (exactCodeMatches.Length == 1)
        {
            plcCode = exactCodeMatches[0].PlcCode.Trim();
            deviceName = exactCodeMatches[0].DeviceName;
            processType = exactCodeMatches[0].ProcessType;
            return true;
        }

        if (exactCodeMatches.Length > 1)
        {
            diagnostic = "capacity_buffer_plc_identity_ambiguous";
            return false;
        }

        if (configuredPlcs.Any(identity => string.Equals(
                identity.DeviceName,
                normalizedIdentity,
                StringComparison.OrdinalIgnoreCase)))
        {
            diagnostic = "capacity_buffer_current_device_name_not_eligible";
            return false;
        }

        var verifiedAliasMatches = configuredPlcs
            .Where(identity => _identityAliasRegistry
                .GetVerifiedAliases(identity.PlcCode)
                .Contains(normalizedIdentity, StringComparer.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (verifiedAliasMatches.Length != 1)
        {
            diagnostic = verifiedAliasMatches.Length == 0
                ? "capacity_buffer_plc_identity_unresolved"
                : "capacity_buffer_plc_identity_ambiguous";
            return false;
        }

        plcCode = verifiedAliasMatches[0].PlcCode.Trim();
        deviceName = verifiedAliasMatches[0].DeviceName;
        processType = verifiedAliasMatches[0].ProcessType;
        return true;
    }

    private async Task<IReadOnlyList<ConfiguredPlcIdentity>> GetConfiguredPlcIdentitiesAsync()
    {
        if (_pluginConfiguration is not null)
        {
            var contextProcessTypes = _contextStore.GetAll()
                .Where(context => context.NetworkDeviceId > 0)
                .GroupBy(context => context.NetworkDeviceId)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var processTypes = group.Select(ResolveProcessType)
                        .Where(process => process is not null)
                        .Distinct(StringComparer.Ordinal)
                        .Take(2)
                        .ToArray();
                        return processTypes.Length == 1 ? processTypes[0] : null;
                    });
            var configuredDevices = _pluginConfiguration.GetPlcs();
            return configuredDevices
                .Where(static device =>
                    !string.IsNullOrWhiteSpace(device.PlcCode)
                    && !string.IsNullOrWhiteSpace(device.DeviceName))
                .Select(device => new ConfiguredPlcIdentity(
                    device.PlcCode.Trim(),
                    device.DeviceName.Trim(),
                    contextProcessTypes.GetValueOrDefault(device.Id)))
                .ToArray();
        }

        return _contextStore.GetAll()
            .Where(static context =>
                !string.IsNullOrWhiteSpace(context.PlcCode)
                && !string.IsNullOrWhiteSpace(context.DeviceName))
            .Select(context => new ConfiguredPlcIdentity(
                context.PlcCode.Trim(),
                context.DeviceName.Trim(),
                ResolveProcessType(context)))
            .ToArray();
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
        string? processType,
        string legacyPlcIdentity,
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
            plcCode,
            deviceName,
            processType,
            legacyPlcIdentity);
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
        string plcCode,
        string? plcName,
        string? processType,
        string legacyPlcIdentity)
    {
        var endMinute = minute == 30 ? 0 : 30;
        var endHour = minute == 30 ? (hour + 1) % 24 : hour;

        var canSendV2 = processType is "ap" or "cp"
                        && !string.IsNullOrWhiteSpace(plcCode)
                        && !string.IsNullOrWhiteSpace(plcName);
        if (canSendV2)
        {
            return new
            {
                deviceId,
                date,
                hour,
                minute,
                timeLabel = $"{hour:D2}:{minute:D2}-{endHour:D2}:{endMinute:D2}",
                shiftCode,
                totalCount,
                okCount = (int?)null,
                ngCount = (int?)null,
                schemaVersion = 2,
                processType,
                plcCode,
                plcName
            };
        }

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
            schemaVersion = 1,
            plcName = legacyPlcIdentity
        };
    }

    private string? ResolveProcessType(ProductionContext context)
    {
        var moduleIds = _contextFactories
            .Where(factory => factory.ContextType.IsInstanceOfType(context))
            .Select(factory => factory.ModuleId.Trim().ToLowerInvariant())
            .Where(moduleId => moduleId is "ap" or "cp")
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return moduleIds.Length == 1 ? moduleIds[0] : null;
    }

    private sealed record ConfiguredPlcIdentity(
        string PlcCode,
        string DeviceName,
        string? ProcessType);

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
