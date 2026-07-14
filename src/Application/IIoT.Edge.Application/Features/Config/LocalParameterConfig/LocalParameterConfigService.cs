using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Features.Config.LocalParameterConfig;

/// <summary>
/// 统一封装本地模块参数读取与变更通知。
/// </summary>
public sealed class LocalParameterConfigService(
    IRepository<SystemConfigEntity> systemConfigs,
    IEdgeCacheService cache)
    : ILocalParameterConfigService, ILocalParameterConfigChangePublisher, ILocalSystemConfigSnapshotReader
{
    private readonly IRepository<SystemConfigEntity> _systemConfigs = systemConfigs;
    private readonly IEdgeCacheService _cache = cache;
    private IReadOnlyList<LocalSystemConfigSnapshot> _currentSystemConfigs = Array.Empty<LocalSystemConfigSnapshot>();

    public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

    public async Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _cache.GetOrCreateAsync<List<SystemConfigEntity>>(
                ParameterCacheKeys.SystemAll,
                async ct => await _systemConfigs.GetListAsync(_ => true, ct).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var snapshots = (result ?? [])
            .OrderBy(x => x.SortOrder)
            .Select(MapSystemConfig)
            .ToArray();
        Volatile.Write(ref _currentSystemConfigs, snapshots);
        return snapshots;
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
        await _systemConfigs.ExecuteDeleteAsync(
            x => x.Key == normalizedKey,
            cancellationToken).ConfigureAwait(false);

        var entity = SystemConfigEntity.Create(normalizedKey, value, description);
        entity.UpdateSortOrder(Math.Max(0, sortOrder));
        _systemConfigs.Add(entity);
        await _systemConfigs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        InvalidateModuleCaches();
    }

    public async Task DeleteSystemConfigAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeModuleParameterKey(key);
        var deleted = await _systemConfigs.ExecuteDeleteAsync(
            x => x.Key == normalizedKey,
            cancellationToken).ConfigureAwait(false);

        if (deleted > 0)
        {
            InvalidateModuleCaches();
        }
    }

    public void NotifyModuleChanged()
        => ParameterConfigChanged?.Invoke(
            this,
            new ParameterConfigChangedEventArgs(ParameterConfigChangeScope.Module));

    private void InvalidateModuleCaches()
    {
        _cache.Remove(ParameterCacheKeys.SystemAll);
        _cache.RemoveByPrefix(ParameterCacheKeys.ModuleSnapshotPrefix);
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

    private static LocalSystemConfigSnapshot MapSystemConfig(SystemConfigEntity entity)
        => new(
            entity.Id,
            entity.Key,
            entity.Value,
            entity.Description,
            entity.SortOrder);
}
