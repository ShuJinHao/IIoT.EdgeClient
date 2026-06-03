using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.ModuleParameters;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class ModuleParamConfigValueStore(
    ILocalParameterConfigService parameterConfigService,
    ModuleParamCategory category,
    string schemaId) : IConfigValueStore
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
