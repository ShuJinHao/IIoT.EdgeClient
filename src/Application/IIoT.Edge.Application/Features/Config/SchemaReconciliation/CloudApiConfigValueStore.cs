using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class CloudApiConfigValueStore(
    IRepository<SystemConfigEntity> systemConfigs,
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
        systemConfigs.Add(entity);
        await systemConfigs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        cache.Remove(ParameterCacheKeys.SystemAll);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!CloudApiConfigParamSchema.IsCloudApiConfigPrefix(key))
        {
            return;
        }

        var deleted = await systemConfigs
            .ExecuteDeleteAsync(x => x.Key == key, cancellationToken)
            .ConfigureAwait(false);
        if (deleted > 0)
        {
            cache.Remove(ParameterCacheKeys.SystemAll);
        }
    }
}
