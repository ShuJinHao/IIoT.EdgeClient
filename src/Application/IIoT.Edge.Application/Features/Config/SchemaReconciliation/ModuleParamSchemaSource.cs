using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class ModuleParamSchemaSource(
    IModuleParamRegistry registry,
    ModuleParamCategory category,
    string schemaId) : IConfigSchemaSource
{
    private const string DescriptionMetadataKey = "Description";
    private const string LegacyDefaultValuesMetadataKey = "LegacyDefaultValues";
    private const string SortOrderMetadataKey = "SortOrder";

    public string SchemaId { get; } = schemaId;

    public Task<IReadOnlyCollection<ConfigSchemaItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = registry.GetDescriptors(category)
            .Select(static descriptor => new ConfigSchemaItem(
                descriptor.StorageKey,
                descriptor.DefaultValue ?? string.Empty,
                BuildMetadata(descriptor)))
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<ConfigSchemaItem>>(items);
    }

    public static string GetDescription(ConfigSchemaItem item)
        => TryGetMetadata(item, DescriptionMetadataKey);

    public static int GetSortOrder(ConfigSchemaItem item)
        => int.TryParse(TryGetMetadata(item, SortOrderMetadataKey), out var value)
            ? value
            : 0;

    public static IReadOnlyCollection<string> GetLegacyDefaultValues(ConfigSchemaItem item)
        => TryGetMetadata(item, LegacyDefaultValuesMetadataKey)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Dictionary<string, string> BuildMetadata(ModuleParamDescriptor descriptor)
    {
        var metadata = new Dictionary<string, string>
        {
            [DescriptionMetadataKey] = descriptor.DescriptionFallback ?? string.Empty,
            [SortOrderMetadataKey] = descriptor.SortOrder.ToString()
        };

        if (descriptor.LegacyDefaultValues is { Count: > 0 })
        {
            metadata[LegacyDefaultValuesMetadataKey] = string.Join('\n', descriptor.LegacyDefaultValues);
        }

        return metadata;
    }

    private static string TryGetMetadata(ConfigSchemaItem item, string key)
        => item.Metadata is not null && item.Metadata.TryGetValue(key, out var value)
            ? value
            : string.Empty;
}
