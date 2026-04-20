using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

public sealed class DataPipelineCapacityGuard
{
    private const string TotalBlockedReason = "total";
    private const string ProcessTypeBlockedReason = "process_type";

    private readonly IOptions<DataPipelineCapacityOptions> _options;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly IMesRetryRecordStore _mesRetryStore;
    private readonly ICloudFallbackBufferStore _cloudFallbackStore;
    private readonly IMesFallbackBufferStore _mesFallbackStore;
    private readonly ICloudUploadDiagnosticsStore _cloudDiagnosticsStore;
    private readonly IMesRetryDiagnosticsStore _mesDiagnosticsStore;
    private readonly ILogService _logger;
    private readonly object _sync = new();

    private CapacityBlockState? _cloudRetryBlock;
    private CapacityBlockState? _cloudFallbackBlock;
    private CapacityBlockState? _mesRetryBlock;
    private CapacityBlockState? _mesFallbackBlock;

    public DataPipelineCapacityGuard(
        IOptions<DataPipelineCapacityOptions> options,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ICloudFallbackBufferStore cloudFallbackStore,
        IMesFallbackBufferStore mesFallbackStore,
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
        IMesRetryDiagnosticsStore mesDiagnosticsStore,
        ILogService logger)
    {
        _options = options;
        _cloudRetryStore = cloudRetryStore;
        _mesRetryStore = mesRetryStore;
        _cloudFallbackStore = cloudFallbackStore;
        _mesFallbackStore = mesFallbackStore;
        _cloudDiagnosticsStore = cloudDiagnosticsStore;
        _mesDiagnosticsStore = mesDiagnosticsStore;
        _logger = logger;
    }

    public async Task<string?> GetCloudRetryBlockReasonAsync(string processType)
        => await EvaluateRetryCapacityAsync(
            channelName: "Cloud",
            processType,
            CapacityBlockedChannel.Retry,
            GetActiveLimits().Cloud,
            _cloudRetryStore.GetCountAsync,
            _cloudRetryStore.GetCountAsync,
            stateAccessor: () => _cloudRetryBlock,
            stateSetter: state => _cloudRetryBlock = state,
            applyDiagnostics: ApplyCloudDiagnosticsState).ConfigureAwait(false);

    public async Task<string?> GetMesRetryBlockReasonAsync(string processType)
        => await EvaluateRetryCapacityAsync(
            channelName: "MES",
            processType,
            CapacityBlockedChannel.Retry,
            GetActiveLimits().Mes,
            _mesRetryStore.GetCountAsync,
            _mesRetryStore.GetCountAsync,
            stateAccessor: () => _mesRetryBlock,
            stateSetter: state => _mesRetryBlock = state,
            applyDiagnostics: ApplyMesDiagnosticsState).ConfigureAwait(false);

    public async Task<string?> GetCloudFallbackBlockReasonAsync(string processType)
        => await EvaluateFallbackCapacityAsync(
            channelName: "Cloud",
            processType,
            GetActiveLimits().Cloud,
            _cloudFallbackStore.GetCountAsync,
            stateAccessor: () => _cloudFallbackBlock,
            stateSetter: state => _cloudFallbackBlock = state,
            applyDiagnostics: ApplyCloudDiagnosticsState).ConfigureAwait(false);

    public async Task<string?> GetMesFallbackBlockReasonAsync(string processType)
        => await EvaluateFallbackCapacityAsync(
            channelName: "MES",
            processType,
            GetActiveLimits().Mes,
            _mesFallbackStore.GetCountAsync,
            stateAccessor: () => _mesFallbackBlock,
            stateSetter: state => _mesFallbackBlock = state,
            applyDiagnostics: ApplyMesDiagnosticsState).ConfigureAwait(false);

    public async Task RefreshCloudRetryCapacityStatusAsync()
        => await RefreshRetryCapacityStatusAsync(
            channelName: "Cloud",
            GetActiveLimits().Cloud,
            _cloudRetryStore.GetCountAsync,
            _cloudRetryStore.GetCountAsync,
            stateAccessor: () => _cloudRetryBlock,
            stateSetter: state => _cloudRetryBlock = state,
            applyDiagnostics: ApplyCloudDiagnosticsState).ConfigureAwait(false);

    public async Task RefreshMesRetryCapacityStatusAsync()
        => await RefreshRetryCapacityStatusAsync(
            channelName: "MES",
            GetActiveLimits().Mes,
            _mesRetryStore.GetCountAsync,
            _mesRetryStore.GetCountAsync,
            stateAccessor: () => _mesRetryBlock,
            stateSetter: state => _mesRetryBlock = state,
            applyDiagnostics: ApplyMesDiagnosticsState).ConfigureAwait(false);

    public async Task RefreshCloudFallbackCapacityStatusAsync()
        => await RefreshFallbackCapacityStatusAsync(
            channelName: "Cloud",
            GetActiveLimits().Cloud,
            _cloudFallbackStore.GetCountAsync,
            stateAccessor: () => _cloudFallbackBlock,
            stateSetter: state => _cloudFallbackBlock = state,
            applyDiagnostics: ApplyCloudDiagnosticsState).ConfigureAwait(false);

    public async Task RefreshMesFallbackCapacityStatusAsync()
        => await RefreshFallbackCapacityStatusAsync(
            channelName: "MES",
            GetActiveLimits().Mes,
            _mesFallbackStore.GetCountAsync,
            stateAccessor: () => _mesFallbackBlock,
            stateSetter: state => _mesFallbackBlock = state,
            applyDiagnostics: ApplyMesDiagnosticsState).ConfigureAwait(false);

    private async Task<string?> EvaluateRetryCapacityAsync(
        string channelName,
        string processType,
        CapacityBlockedChannel blockedChannel,
        DataPipelineChannelCapacityOptions limits,
        Func<Task<int>> getTotalCountAsync,
        Func<string, Task<int>> getProcessCountAsync,
        Func<CapacityBlockState?> stateAccessor,
        Action<CapacityBlockState?> stateSetter,
        Action applyDiagnostics)
    {
        if (limits.RetryTotalLimit > 0)
        {
            var totalCount = await getTotalCountAsync().ConfigureAwait(false);
            if (totalCount >= limits.RetryTotalLimit)
            {
                MarkCapacityBlocked(
                    channelName,
                    blockedChannel,
                    TotalBlockedReason,
                    processType: null,
                    currentCount: totalCount,
                    limit: limits.RetryTotalLimit,
                    stateAccessor,
                    stateSetter,
                    applyDiagnostics);
                return TotalBlockedReason;
            }
        }

        if (limits.RetryPerProcessTypeLimit > 0)
        {
            var processCount = await getProcessCountAsync(processType).ConfigureAwait(false);
            if (processCount >= limits.RetryPerProcessTypeLimit)
            {
                MarkCapacityBlocked(
                    channelName,
                    blockedChannel,
                    ProcessTypeBlockedReason,
                    processType,
                    processCount,
                    limits.RetryPerProcessTypeLimit,
                    stateAccessor,
                    stateSetter,
                    applyDiagnostics);
                return ProcessTypeBlockedReason;
            }
        }

        return null;
    }

    private async Task<string?> EvaluateFallbackCapacityAsync(
        string channelName,
        string processType,
        DataPipelineChannelCapacityOptions limits,
        Func<Task<int>> getTotalCountAsync,
        Func<CapacityBlockState?> stateAccessor,
        Action<CapacityBlockState?> stateSetter,
        Action applyDiagnostics)
    {
        if (limits.FallbackTotalLimit <= 0)
        {
            return null;
        }

        var totalCount = await getTotalCountAsync().ConfigureAwait(false);
        if (totalCount < limits.FallbackTotalLimit)
        {
            return null;
        }

        MarkCapacityBlocked(
            channelName,
            CapacityBlockedChannel.Fallback,
            TotalBlockedReason,
            processType: null,
            currentCount: totalCount,
            limit: limits.FallbackTotalLimit,
            stateAccessor,
            stateSetter,
            applyDiagnostics);
        return TotalBlockedReason;
    }

    private async Task RefreshRetryCapacityStatusAsync(
        string channelName,
        DataPipelineChannelCapacityOptions limits,
        Func<Task<int>> getTotalCountAsync,
        Func<string, Task<int>> getProcessCountAsync,
        Func<CapacityBlockState?> stateAccessor,
        Action<CapacityBlockState?> stateSetter,
        Action applyDiagnostics)
    {
        var state = stateAccessor();
        if (state is null || state.Channel != CapacityBlockedChannel.Retry)
        {
            return;
        }

        var recovered = state.BlockedReason switch
        {
            TotalBlockedReason when limits.RetryTotalLimit > 0
                => await getTotalCountAsync().ConfigureAwait(false) < limits.RetryTotalLimit,
            ProcessTypeBlockedReason when limits.RetryPerProcessTypeLimit > 0 && !string.IsNullOrWhiteSpace(state.ProcessType)
                => await getProcessCountAsync(state.ProcessType).ConfigureAwait(false) < limits.RetryPerProcessTypeLimit,
            _ => true
        };

        if (recovered)
        {
            ClearCapacityBlocked(channelName, state, stateSetter, applyDiagnostics);
        }
    }

    private async Task RefreshFallbackCapacityStatusAsync(
        string channelName,
        DataPipelineChannelCapacityOptions limits,
        Func<Task<int>> getTotalCountAsync,
        Func<CapacityBlockState?> stateAccessor,
        Action<CapacityBlockState?> stateSetter,
        Action applyDiagnostics)
    {
        var state = stateAccessor();
        if (state is null || state.Channel != CapacityBlockedChannel.Fallback)
        {
            return;
        }

        if (limits.FallbackTotalLimit > 0
            && await getTotalCountAsync().ConfigureAwait(false) >= limits.FallbackTotalLimit)
        {
            return;
        }

        ClearCapacityBlocked(channelName, state, stateSetter, applyDiagnostics);
    }

    private void MarkCapacityBlocked(
        string channelName,
        CapacityBlockedChannel blockedChannel,
        string blockedReason,
        string? processType,
        int currentCount,
        int limit,
        Func<CapacityBlockState?> stateAccessor,
        Action<CapacityBlockState?> stateSetter,
        Action applyDiagnostics)
    {
        var existing = stateAccessor();
        if (existing is not null
            && existing.Channel == blockedChannel
            && string.Equals(existing.BlockedReason, blockedReason, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.ProcessType, processType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_sync)
        {
            stateSetter(new CapacityBlockState(
                blockedChannel,
                blockedReason,
                processType,
                DateTime.UtcNow));
            applyDiagnostics();
        }

        var processText = string.IsNullOrWhiteSpace(processType) ? "--" : processType;
        _logger.Warn(
            $"[CapacityGuard] {channelName} {blockedChannel} channel is blocked by {blockedReason}. ProcessType={processText}, Current={currentCount}, Limit={limit}.");
    }

    private void ClearCapacityBlocked(
        string channelName,
        CapacityBlockState existingState,
        Action<CapacityBlockState?> stateSetter,
        Action applyDiagnostics)
    {
        lock (_sync)
        {
            stateSetter(null);
            applyDiagnostics();
        }

        _logger.Info(
            $"[CapacityGuard] {channelName} {existingState.Channel} channel recovered from {existingState.BlockedReason} capacity block.");
    }

    private void ApplyCloudDiagnosticsState()
    {
        var active = _cloudRetryBlock ?? _cloudFallbackBlock;
        if (active is null)
        {
            _cloudDiagnosticsStore.ClearCapacityBlocked();
            return;
        }

        _cloudDiagnosticsStore.MarkCapacityBlocked(
            active.Channel,
            active.BlockedReason,
            active.ProcessType,
            active.OccurredAt);
    }

    private void ApplyMesDiagnosticsState()
    {
        var active = _mesRetryBlock ?? _mesFallbackBlock;
        if (active is null)
        {
            _mesDiagnosticsStore.ClearCapacityBlocked();
            return;
        }

        _mesDiagnosticsStore.MarkCapacityBlocked(
            active.Channel,
            active.BlockedReason,
            active.ProcessType,
            active.OccurredAt);
    }

    private DataPipelineCapacityOptions GetActiveLimits() => _options.Value;

    private sealed record CapacityBlockState(
        CapacityBlockedChannel Channel,
        string BlockedReason,
        string? ProcessType,
        DateTime OccurredAt);
}
