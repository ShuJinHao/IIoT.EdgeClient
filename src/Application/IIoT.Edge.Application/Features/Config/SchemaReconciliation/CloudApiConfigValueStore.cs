using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class CloudApiConfigValueStore(
    IReadRepository<SystemConfigEntity> systemConfigs,
    IEdgeUnitOfWorkFactory unitOfWorkFactory,
    IEdgeCacheService cache) : IConfigValueStore
{
    public string SchemaId => CloudApiConfigSchemaIds.CloudApi;

    public async Task<IReadOnlyCollection<string>> GetExistingKeysAsync(CancellationToken cancellationToken = default)
        => (await systemConfigs
                .GetListAsync(static x => x.Key.StartsWith(CloudApiConfigParamSchema.KeyPrefix), cancellationToken)
                .ConfigureAwait(false))
            .Select(static x => x.Key)
            .ToArray();

    public async Task InsertAsync(ConfigSchemaItem item, CancellationToken cancellationToken = default)
    {
        if (!CloudApiConfigParamSchema.IsCloudApiConfigKey(item.Key))
        {
            return;
        }

        var entity = SystemConfigEntity.Create(
            item.Key,
            item.DefaultValue,
            CloudApiConfigSchemaSource.GetDescription(item));
        entity.UpdateSortOrder(CloudApiConfigSchemaSource.GetSortOrder(item));
        await using var unitOfWork = await unitOfWorkFactory
            .BeginAsync(cancellationToken)
            .ConfigureAwait(false);
        unitOfWork.Repository<SystemConfigEntity>().Add(entity);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        cache.Remove(ParameterCacheKeys.SystemAll);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!CloudApiConfigParamSchema.IsCloudApiConfigPrefix(key))
        {
            return;
        }

        await using var unitOfWork = await unitOfWorkFactory
            .BeginAsync(cancellationToken)
            .ConfigureAwait(false);
        var repository = unitOfWork.Repository<SystemConfigEntity>();
        var existing = await repository
            .GetListAsync(x => x.Key == key, cancellationToken)
            .ConfigureAwait(false);
        foreach (var config in existing)
        {
            repository.Delete(config);
        }

        if (existing.Count > 0)
        {
            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            cache.Remove(ParameterCacheKeys.SystemAll);
        }
    }
}
