using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Application.Features.Config.LocalParameterConfig;

public sealed class LocalSystemRuntimeConfigService(
    ILocalParameterConfigService parameterConfigService,
    ILogService logger)
    : ILocalSystemRuntimeConfigService, IDisposable
{
    private const int DefaultIntervalSeconds = 60;

    private readonly ILocalParameterConfigService _parameterConfigService = parameterConfigService;
    private readonly ILogService _logger = logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public SystemRuntimeConfigSnapshot Current { get; private set; } = SystemRuntimeConfigSnapshot.Default;

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        _parameterConfigService.ParameterConfigChanged -= OnParameterConfigChanged;
        _parameterConfigService.ParameterConfigChanged += OnParameterConfigChanged;
    }

    public void Dispose()
    {
        _parameterConfigService.ParameterConfigChanged -= OnParameterConfigChanged;
        _refreshGate.Dispose();
    }

    private void OnParameterConfigChanged(object? sender, ParameterConfigChangedEventArgs args)
    {
        if (args.Scope != ParameterConfigChangeScope.System)
        {
            return;
        }

        _ = RefreshAfterChangeAsync();
    }

    private async Task RefreshAfterChangeAsync()
    {
        try
        {
            await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[LocalSystemRuntimeConfig] Failed to refresh system runtime config: {ex.Message}");
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var systemConfigs = await _parameterConfigService
                .GetSystemConfigsAsync(cancellationToken)
                .ConfigureAwait(false);
            var values = systemConfigs.ToDictionary(
                static x => x.Key,
                static x => x.Value,
                StringComparer.OrdinalIgnoreCase);

            Current = new SystemRuntimeConfigSnapshot(
                ParseMesBaseUrl(values),
                ParseBoolean(values, SystemConfigKey.启用MES上报, defaultValue: true),
                ParsePositiveSeconds(values, SystemConfigKey.心跳间隔, DefaultIntervalSeconds),
                ParsePositiveSeconds(values, SystemConfigKey.云端同步周期, DefaultIntervalSeconds));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static string? ParseMesBaseUrl(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue(SystemConfigKey.MES服务地址.ToString(), out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var trimmed = configured.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out _)
            ? trimmed
            : null;
    }

    private static bool ParseBoolean(
        IReadOnlyDictionary<string, string> values,
        SystemConfigKey key,
        bool defaultValue)
    {
        if (!values.TryGetValue(key.ToString(), out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            return defaultValue;
        }

        return bool.TryParse(configured.Trim(), out var parsed)
            ? parsed
            : defaultValue;
    }

    private static TimeSpan ParsePositiveSeconds(
        IReadOnlyDictionary<string, string> values,
        SystemConfigKey key,
        int defaultSeconds)
    {
        if (!values.TryGetValue(key.ToString(), out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            return TimeSpan.FromSeconds(defaultSeconds);
        }

        return int.TryParse(configured.Trim(), out var parsed) && parsed > 0
            ? TimeSpan.FromSeconds(parsed)
            : TimeSpan.FromSeconds(defaultSeconds);
    }
}
