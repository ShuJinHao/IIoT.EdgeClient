using IIoT.Edge.Module.Contracts.Cache;
using IIoT.Edge.Application.Features.Config;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Queries;

/// <summary>
/// 查询：获取全部本地配置记录。
/// </summary>
public record GetAllSystemConfigsQuery() : IQuery<Result<List<SystemConfigEntity>>>;

public class GetAllSystemConfigsHandler(
    IReadRepository<SystemConfigEntity> repo,
    IEdgeCacheService cache
) : IQueryHandler<GetAllSystemConfigsQuery, Result<List<SystemConfigEntity>>>
{
    public async Task<Result<List<SystemConfigEntity>>> Handle(
        GetAllSystemConfigsQuery request,
        CancellationToken cancellationToken)
    {
        var list = await cache.GetOrCreateAsync<List<SystemConfigEntity>>(
                ParameterCacheKeys.SystemAll,
                async ct => await repo.GetListAsync(_ => true, ct).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(list ?? []);
    }
}
