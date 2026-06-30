using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Config.CloudApi;

namespace IIoT.Edge.Application.Features.Config.LocalParameterConfig;

public sealed class LocalSystemRuntimeConfigService(
    ILocalParameterConfigService parameterConfigService,
    IModuleParamRoleProvider moduleParamRoleProvider,
    IProcessIntegrationRegistry processIntegrationRegistry,
    ILogService logger)
    : ILocalSystemRuntimeConfigService, IDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);
    public const string RuntimeHeartbeatIntervalSecondsKey = "RuntimeHeartbeatIntervalSeconds";

    private readonly ILocalParameterConfigService _parameterConfigService = parameterConfigService;
    private readonly IModuleParamRoleProvider _moduleParamRoleProvider = moduleParamRoleProvider;
    private readonly IProcessIntegrationRegistry _processIntegrationRegistry = processIntegrationRegistry;
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
        if (args.Scope != ParameterConfigChangeScope.Module)
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
            _logger.Warn($"[本地参数] 刷新运行参数失败：{ex.Message}");
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mesProcessTypes = _processIntegrationRegistry
                .GetMesUploaders()
                .Keys
                .ToArray();
            var mesEnabled = await _moduleParamRoleProvider
                .AnyMesBoolAsync(
                    ModuleParamRole.MesEnabled,
                    mesProcessTypes,
                    defaultValue: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var systemCloudEnabled = await ReadSystemCloudEnabledAsync(cancellationToken).ConfigureAwait(false);
            var runtimeHeartbeatInterval = await ReadRuntimeHeartbeatIntervalAsync(cancellationToken).ConfigureAwait(false);
            Current = new SystemRuntimeConfigSnapshot(
                mesEnabled,
                systemCloudEnabled,
                DefaultInterval,
                DefaultInterval,
                runtimeHeartbeatInterval);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<bool> ReadSystemCloudEnabledAsync(CancellationToken cancellationToken)
    {
        var configs = await _parameterConfigService
            .GetSystemConfigsAsync(cancellationToken)
            .ConfigureAwait(false);
        var value = configs.FirstOrDefault(static x =>
            string.Equals(x.Key, CloudApiConfigParamSchema.Enabled, StringComparison.OrdinalIgnoreCase))?.Value;
        return string.IsNullOrWhiteSpace(value) || !bool.TryParse(value.Trim(), out var enabled)
            ? true
            : enabled;
    }

    private async Task<TimeSpan> ReadRuntimeHeartbeatIntervalAsync(CancellationToken cancellationToken)
    {
        var configs = await _parameterConfigService
            .GetSystemConfigsAsync(cancellationToken)
            .ConfigureAwait(false);
        var value = configs.FirstOrDefault(static x =>
            string.Equals(x.Key, RuntimeHeartbeatIntervalSecondsKey, StringComparison.OrdinalIgnoreCase))?.Value;
        return int.TryParse(value?.Trim(), out var seconds) && seconds is >= 10 and <= 3600
            ? TimeSpan.FromSeconds(seconds)
            : DefaultInterval;
    }
}
