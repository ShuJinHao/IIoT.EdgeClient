using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.ModuleParameters;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class ModuleParamConfigValueStore(
    ILocalParameterConfigService parameterConfigService,
    ModuleParamCategory category,
    string schemaId) : IConfigValueStore, IRepairableConfigValueStore
{
    public string SchemaId { get; } = schemaId;

    public async Task<IReadOnlyCollection<string>> GetExistingKeysAsync(CancellationToken cancellationToken = default)
        => (await parameterConfigService.GetSystemConfigsAsync(cancellationToken).ConfigureAwait(false))
            .Where(snapshot => IsCategoryKey(snapshot.Key))
            .Select(static snapshot => snapshot.Key)
            .ToList();

    public Task InsertAsync(ConfigSchemaItem item, CancellationToken cancellationToken = default)
        => parameterConfigService.InsertSystemConfigAsync(
            item.Key,
            item.DefaultValue,
            ModuleParamSchemaSource.GetDescription(item),
            ModuleParamSchemaSource.GetSortOrder(item),
            cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        => parameterConfigService.DeleteSystemConfigAsync(key, cancellationToken);

    public async Task RepairExistingAsync(ConfigSchemaItem item, CancellationToken cancellationToken = default)
    {
        var legacyDefaultValues = ModuleParamSchemaSource.GetLegacyDefaultValues(item);
        if (legacyDefaultValues.Count == 0)
        {
            return;
        }

        var current = (await parameterConfigService.GetSystemConfigsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(snapshot => string.Equals(snapshot.Key, item.Key, StringComparison.OrdinalIgnoreCase));
        if (current is null
            || string.Equals(current.Value, item.DefaultValue, StringComparison.Ordinal)
            || !legacyDefaultValues.Contains(current.Value, StringComparer.Ordinal))
        {
            return;
        }

        await InsertAsync(item, cancellationToken).ConfigureAwait(false);
    }

    private bool IsCategoryKey(string key)
    {
        if (!ModuleParamKeys.IsModuleStorageKey(key))
        {
            return false;
        }

        var segments = key.Split(':', 4);
        return segments.Length == 4
               && Enum.TryParse<ModuleParamCategory>(segments[2], ignoreCase: true, out var parsedCategory)
               && parsedCategory == category;
    }
}
