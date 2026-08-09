using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Module.Contracts.Plugins;

namespace IIoT.Edge.Application.Features.Config.LocalParameterConfig;

/// <summary>
/// 统一封装本地模块参数读取与变更通知。
/// </summary>
public sealed class LocalParameterConfigService(
    IDevicePluginConfigurationSnapshotAccessor snapshots,
    IEnumerable<IDevicePluginConfigurationStoreV1> stores)
    : ILocalParameterConfigService, ILocalParameterConfigChangePublisher, ILocalSystemConfigSnapshotReader
{
    private readonly IDevicePluginConfigurationStoreV1[] _stores = stores.ToArray();
    private IReadOnlyList<LocalSystemConfigSnapshot> _currentSystemConfigs = [];

    public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

    public Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = snapshots.GetRequiredSnapshot().ModuleSettings
            .OrderBy(static item => item.SortOrder)
            .Select(static item => new LocalSystemConfigSnapshot(
                DevicePluginProjectionIds.Setting(item.Key),
                item.Key,
                item.Value,
                item.DisplayName,
                item.SortOrder))
            .ToArray();
        Volatile.Write(ref _currentSystemConfigs, current);
        return Task.FromResult<IReadOnlyList<LocalSystemConfigSnapshot>>(current);
    }

    public IReadOnlyList<LocalSystemConfigSnapshot> GetCurrentSystemConfigs()
        => Volatile.Read(ref _currentSystemConfigs);

    public async Task InsertSystemConfigAsync(
        string key,
        string value,
        string? description = null,
        int sortOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeModuleParameterKey(key);
        var snapshot = snapshots.GetRequiredSnapshot();
        var settings = snapshot.ModuleSettings
            .Where(item => !string.Equals(item.Key, normalizedKey, StringComparison.OrdinalIgnoreCase))
            .Append(new DevicePluginModuleSetting(
                normalizedKey,
                value ?? string.Empty,
                description,
                Unit: null,
                Math.Max(0, sortOrder)))
            .OrderBy(static item => item.SortOrder)
            .ToArray();
        await WriteAsync(settings, snapshot.ConfigurationVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteSystemConfigAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeModuleParameterKey(key);
        var snapshot = snapshots.GetRequiredSnapshot();
        var settings = snapshot.ModuleSettings
            .Where(item => !string.Equals(item.Key, normalizedKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (settings.Length == snapshot.ModuleSettings.Count)
        {
            return;
        }

        await WriteAsync(settings, snapshot.ConfigurationVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    public void NotifyModuleChanged()
        => ParameterConfigChanged?.Invoke(
            this,
            new ParameterConfigChangedEventArgs(ParameterConfigChangeScope.Module));

    private async Task WriteAsync(
        IReadOnlyList<DevicePluginModuleSetting> settings,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (_stores.Length != 1)
        {
            throw new InvalidOperationException("PLUGIN_DATABASE_PORT_CARDINALITY_INVALID");
        }

        var result = await _stores[0]
            .UpdateModuleSettingsAsync(settings, expectedVersion, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                result.FailureReasonCode ?? "PLUGIN_MODULE_SETTINGS_WRITE_REJECTED");
        }

        await snapshots.RefreshAsync(cancellationToken).ConfigureAwait(false);
        _ = await GetSystemConfigsAsync(cancellationToken).ConfigureAwait(false);
        NotifyModuleChanged();
    }

    private static string NormalizeModuleParameterKey(string key)
    {
        var normalized = key?.Trim() ?? string.Empty;
        if (!ModuleParamKeys.IsModuleStorageKey(normalized))
        {
            throw new ArgumentException("插件参数键必须以 Module: 开头。", nameof(key));
        }

        return normalized;
    }
}
