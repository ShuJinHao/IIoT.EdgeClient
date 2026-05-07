namespace IIoT.Edge.Application.Features.Hardware.IoMappings;

/// <summary>
/// IO 映射展示分组规则。硬件配置和 IO 交互必须共用同一套分类语义。
/// </summary>
public static class IoMappingDisplay
{
    public const string InteractionCategory = IoMappingOptionCatalog.CategoryInteraction;
    public const string SingleReadCategory = IoMappingOptionCatalog.CategorySingleRead;
    public const string ContinuousReadCategory = IoMappingOptionCatalog.CategoryContinuousRead;
    public const string SingleWriteCategory = IoMappingOptionCatalog.CategorySingleWrite;
    public const string ContinuousWriteCategory = IoMappingOptionCatalog.CategoryContinuousWrite;

    public static string ResolveCategory(string? category, int addressCount)
        => IoMappingOptionCatalog.NormalizeCategory(category, addressCount);

    public static string ResolveBusinessGroup(string? businessGroup, string category)
        => string.IsNullOrWhiteSpace(businessGroup)
            ? category
            : businessGroup.Trim();

    public static bool IsContinuousMatrix(string? dataType, int addressCount)
        => addressCount > 1
           && !string.Equals(dataType, "Ascii", StringComparison.OrdinalIgnoreCase);

    public static string BuildSectionTitle(string? category, string? businessGroup)
        => ResolveCategory(category, addressCount: 1);
}
