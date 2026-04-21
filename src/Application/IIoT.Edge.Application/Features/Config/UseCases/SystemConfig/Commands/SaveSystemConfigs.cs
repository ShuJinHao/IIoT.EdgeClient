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
        var valid = request.Configs
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key)
            .Select(g => g.Last())
            .ToList();

        await repo.ExecuteDeleteAsync(_ => true);

        for (int i = 0; i < valid.Count; i++)
        {
            var c = valid[i];
            repo.Add(new SystemConfigEntity(c.Key, c.Value, c.Description)
            {
                SortOrder = i + 1
            });
        }
        await repo.SaveChangesAsync(cancellationToken);

        cache.Remove(CacheKey);
        changePublisher.NotifySystemChanged();
        return Result.Success();
    }
}
