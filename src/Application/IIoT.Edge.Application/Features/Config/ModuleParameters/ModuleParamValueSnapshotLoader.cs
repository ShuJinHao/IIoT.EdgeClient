using IIoT.Edge.Module.Contracts.Cache;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Application.Features.Config;

namespace IIoT.Edge.Application.Features.Config.ModuleParameters;

/// <summary>
/// 模块参数快照加载器，统一处理本地参数表读取和缓存。
/// </summary>
public sealed class ModuleParamValueSnapshotLoader(
    ILocalParameterConfigService localParameterConfigService,
    IEdgeCacheService cache)
{
    public async Task<ModuleParamValueSnapshot> LoadAsync(
        string moduleId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        var snapshot = await cache.GetOrCreateAsync(
                ParameterCacheKeys.ModuleSnapshot(moduleId),
                ct => LoadFromStoreAsync(moduleId, ct),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return snapshot ?? new ModuleParamValueSnapshot(moduleId, new Dictionary<string, string>());
    }

    private async Task<ModuleParamValueSnapshot?> LoadFromStoreAsync(
        string moduleId,
        CancellationToken cancellationToken)
    {
        var prefix = $"{ModuleParamKeys.StoragePrefix}{moduleId}:";
        var values = (await localParameterConfigService
                .GetSystemConfigsAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static x => x.Key,
                static x => x.Value,
                StringComparer.OrdinalIgnoreCase);

        return new ModuleParamValueSnapshot(moduleId, values);
    }
}
