using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

internal static class IoMappingSchemaMetadata
{
    private const string NetworkDeviceIdKey = "NetworkDeviceId";
    private const string SignalKeyKey = "SignalKey";
    private const string AddressCountKey = "AddressCount";
    private const string DataTypeKey = "DataType";
    private const string DirectionKey = "Direction";
    private const string CategoryKey = "Category";
    private const string BusinessGroupKey = "BusinessGroup";
    private const string SortOrderKey = "SortOrder";
    private const string RemarkKey = "Remark";

    public static IReadOnlyDictionary<string, string> Create(int networkDeviceId, ModuleIoTemplateEntry template)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NetworkDeviceIdKey] = networkDeviceId.ToString(),
            [SignalKeyKey] = template.SignalKey,
            [AddressCountKey] = Math.Max(1, template.AddressCount).ToString(),
            [DataTypeKey] = string.IsNullOrWhiteSpace(template.DataType)
                ? IoMappingOptionCatalog.DataTypeInt16
                : template.DataType.Trim(),
            [DirectionKey] = string.IsNullOrWhiteSpace(template.Direction)
                ? IoMappingOptionCatalog.DirectionRead
                : template.Direction.Trim(),
            [CategoryKey] = IoMappingOptionCatalog.NormalizeCategory(template.Category, template.AddressCount),
            [BusinessGroupKey] = template.BusinessGroup?.Trim() ?? string.Empty,
            [SortOrderKey] = Math.Max(0, template.SortOrder).ToString(),
            [RemarkKey] = template.Remark?.Trim() ?? string.Empty
        };

    public static int GetNetworkDeviceId(ConfigSchemaItem item)
        => GetInt(item, NetworkDeviceIdKey, 0);

    public static string GetSignalKey(ConfigSchemaItem item)
        => GetRequired(item, SignalKeyKey);

    public static int GetAddressCount(ConfigSchemaItem item)
        => Math.Max(1, GetInt(item, AddressCountKey, 1));

    public static string GetDataType(ConfigSchemaItem item)
        => GetOptional(item, DataTypeKey, IoMappingOptionCatalog.DataTypeInt16);

    public static string GetDirection(ConfigSchemaItem item)
        => GetOptional(item, DirectionKey, IoMappingOptionCatalog.DirectionRead);

    public static string GetCategory(ConfigSchemaItem item)
        => GetOptional(item, CategoryKey, IoMappingOptionCatalog.CategorySingleRead);

    public static string GetBusinessGroup(ConfigSchemaItem item)
        => GetOptional(item, BusinessGroupKey, string.Empty);

    public static int GetSortOrder(ConfigSchemaItem item)
        => Math.Max(0, GetInt(item, SortOrderKey, 0));

    public static string? GetRemark(ConfigSchemaItem item)
    {
        var value = GetOptional(item, RemarkKey, string.Empty);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string GetRequired(ConfigSchemaItem item, string key)
    {
        if (item.Metadata is null
            || !item.Metadata.TryGetValue(key, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"IO schema item '{item.Key}' missing metadata '{key}'.");
        }

        return value.Trim();
    }

    private static string GetOptional(ConfigSchemaItem item, string key, string fallback)
        => item.Metadata is not null
           && item.Metadata.TryGetValue(key, out var value)
           && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;

    private static int GetInt(ConfigSchemaItem item, string key, int fallback)
        => item.Metadata is not null
           && item.Metadata.TryGetValue(key, out var value)
           && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
}
