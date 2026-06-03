using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.CloudApi;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class CloudApiConfigSchemaSource(
    ICloudApiConfigSnapshotProvider cloudApiConfigSnapshotProvider) : IConfigSchemaSource
{
    private const string DescriptionMetadataKey = "Description";
    private const string SortOrderMetadataKey = "SortOrder";

    public string SchemaId => CloudApiConfigSchemaIds.CloudApi;

    public Task<IReadOnlyCollection<ConfigSchemaItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = cloudApiConfigSnapshotProvider.GetCurrent();
        var items = CloudApiConfigParamSchema.Descriptors
            .Select(descriptor => new ConfigSchemaItem(
                descriptor.Key,
                CloudApiConfigParamSchema.GetDefaultValue(descriptor.Key, snapshot),
                new Dictionary<string, string>
                {
                    [DescriptionMetadataKey] = descriptor.DescriptionFallback,
                    [SortOrderMetadataKey] = descriptor.SortOrder.ToString()
                }))
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<ConfigSchemaItem>>(items);
    }

    public static string GetDescription(ConfigSchemaItem item)
        => TryGetMetadata(item, DescriptionMetadataKey);

    public static int GetSortOrder(ConfigSchemaItem item)
        => int.TryParse(TryGetMetadata(item, SortOrderMetadataKey), out var value)
            ? value
            : 0;

    private static string TryGetMetadata(ConfigSchemaItem item, string key)
        => item.Metadata is not null && item.Metadata.TryGetValue(key, out var value)
            ? value
            : string.Empty;
}

public static class CloudApiConfigSchemaIds
{
    public const string CloudApi = "cloud-api";
}
