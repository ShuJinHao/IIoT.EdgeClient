using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Queries;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Repository;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Application.Features.Config.LocalParameterConfig;

/// <summary>
/// 统一封装本地模块参数读取与变更通知。
/// </summary>
public sealed class LocalParameterConfigService(
    IServiceScopeFactory scopeFactory)
    : ILocalParameterConfigService, ILocalParameterConfigChangePublisher
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

    public async Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var result = await sender.Send(new GetAllSystemConfigsQuery(), cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return [];
        }

        return result.Value
            .OrderBy(x => x.SortOrder)
            .Select(MapSystemConfig)
            .ToList();
    }

    public async Task InsertSystemConfigAsync(
        string key,
        string value,
        string? description = null,
        int sortOrder = 0,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeModuleParameterKey(key);
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<SystemConfigEntity>>();
        await repo.ExecuteDeleteAsync(
            x => x.Key == normalizedKey,
            cancellationToken).ConfigureAwait(false);

        var entity = SystemConfigEntity.Create(normalizedKey, value, description);
        entity.UpdateSortOrder(Math.Max(0, sortOrder));
        repo.Add(entity);
        await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        InvalidateModuleCaches(scope.ServiceProvider);
    }

    public async Task DeleteSystemConfigAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeModuleParameterKey(key);
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<SystemConfigEntity>>();
        var deleted = await repo.ExecuteDeleteAsync(
            x => x.Key == normalizedKey,
            cancellationToken).ConfigureAwait(false);

        if (deleted > 0)
        {
            InvalidateModuleCaches(scope.ServiceProvider);
        }
    }

    public void NotifyModuleChanged()
        => ParameterConfigChanged?.Invoke(
            this,
            new ParameterConfigChangedEventArgs(ParameterConfigChangeScope.Module));

    private void InvalidateModuleCaches(IServiceProvider serviceProvider)
    {
        var cache = serviceProvider.GetRequiredService<IEdgeCacheService>();
        cache.Remove(ParameterCacheKeys.SystemAll);
        cache.RemoveByPrefix(ParameterCacheKeys.ModuleSnapshotPrefix);
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
