namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class ConfigSchemaReconciler(
    IEnumerable<IConfigSchemaSource> sources,
    IEnumerable<IConfigValueStore> stores) : IConfigSchemaReconciler
{
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var storesBySchemaId = stores
            .GroupBy(static store => store.SchemaId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!storesBySchemaId.TryGetValue(source.SchemaId, out var store))
            {
                continue;
            }

            await ReconcileSourceAsync(source, store, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReconcileSourceAsync(
        IConfigSchemaSource source,
        IConfigValueStore store,
        CancellationToken cancellationToken)
    {
        var desiredItems = (await source.GetItemsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(static item => item.Key, static item => item, StringComparer.Ordinal);
        var existingKeys = await store.GetExistingKeysAsync(cancellationToken).ConfigureAwait(false);
        var existingKeySet = existingKeys.ToHashSet(StringComparer.Ordinal);

        foreach (var item in desiredItems.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!existingKeySet.Contains(item.Key))
            {
                await store.InsertAsync(item, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (store is IRepairableConfigValueStore repairableStore)
            {
                await repairableStore.RepairExistingAsync(item, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var existingKey in existingKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!desiredItems.ContainsKey(existingKey))
            {
                await store.DeleteAsync(existingKey, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
