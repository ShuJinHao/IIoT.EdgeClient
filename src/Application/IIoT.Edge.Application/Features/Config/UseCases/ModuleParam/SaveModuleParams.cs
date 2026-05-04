using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Features.Config.UseCases.ModuleParam;

/// <summary>
/// 单条插件参数的数据传输对象。
/// </summary>
public sealed record ModuleParamDto(
    string Key,
    string Value,
    string? Description = null);

/// <summary>
/// 保存插件枚举参数，只覆盖传入 key 对应的模块参数。
/// </summary>
public sealed record SaveModuleParamsCommand(
    List<ModuleParamDto> Params) : ICommand<Result>;

public sealed class SaveModuleParamsHandler(
    IRepository<SystemConfigEntity> repo,
    IEdgeCacheService cache,
    ILocalParameterConfigChangePublisher changePublisher)
    : ICommandHandler<SaveModuleParamsCommand, Result>
{
    public async Task<Result> Handle(
        SaveModuleParamsCommand request,
        CancellationToken cancellationToken)
    {
        List<SystemConfigEntity> configs;
        try
        {
            configs = request.Params
                .GroupBy(x => x.Key?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .Select((dto, index) =>
                {
                    var key = dto.Key?.Trim() ?? string.Empty;
                    if (!ModuleParamKeys.IsModuleStorageKey(key))
                    {
                        throw new ArgumentException("插件参数键必须以 Module: 开头。");
                    }

                    var entity = SystemConfigEntity.Create(key, dto.Value, dto.Description);
                    entity.UpdateSortOrder(index + 1);
                    return entity;
                })
                .ToList();
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message);
        }

        var keys = configs
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count > 0)
        {
            await repo.ExecuteDeleteAsync(x => keys.Contains(x.Key), cancellationToken);
        }

        foreach (var config in configs)
        {
            repo.Add(config);
        }

        await repo.SaveChangesAsync(cancellationToken);

        cache.Remove(ParameterCacheKeys.SystemAll);
        cache.RemoveByPrefix(ParameterCacheKeys.ModuleSnapshotPrefix);
        changePublisher.NotifyModuleChanged();
        return Result.Success();
    }
}
