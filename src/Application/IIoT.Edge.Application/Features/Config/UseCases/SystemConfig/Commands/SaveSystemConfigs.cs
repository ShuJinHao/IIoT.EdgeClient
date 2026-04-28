using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Domain.Config.Aggregates;

namespace IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Commands;

/// <summary>
/// 单条系统配置的数据传输对象。
/// </summary>
public record SystemConfigDto(
    string Key,
    string Value,
    string? Description = null
);

/// <summary>
/// 命令：保存系统配置，采用全量覆盖方式。
/// </summary>
public record SaveSystemConfigsCommand(
    List<SystemConfigDto> Configs
) : ICommand<Result>;

public class SaveSystemConfigsHandler(
    IRepository<SystemConfigEntity> repo,
    IEdgeCacheService cache,
    ILocalParameterConfigChangePublisher changePublisher
) : ICommandHandler<SaveSystemConfigsCommand, Result>
{
    private const string CacheKey = "Config:SystemAll";

    public async Task<Result> Handle(
        SaveSystemConfigsCommand request,
        CancellationToken cancellationToken)
    {
        List<SystemConfigEntity> configs;
        try
        {
            configs = request.Configs
                .GroupBy(x => x.Key?.Trim() ?? string.Empty)
                .Select(g => g.Last())
                .Select((dto, index) =>
                {
                    var entity = SystemConfigEntity.Create(dto.Key, dto.Value, dto.Description);
                    entity.UpdateSortOrder(index + 1);
                    return entity;
                })
                .ToList();
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message);
        }

        await repo.ExecuteDeleteAsync(_ => true, cancellationToken);

        foreach (var config in configs)
        {
            repo.Add(config);
        }

        await repo.SaveChangesAsync(cancellationToken);

        cache.Remove(CacheKey);
        changePublisher.NotifySystemChanged();
        return Result.Success();
    }
}
