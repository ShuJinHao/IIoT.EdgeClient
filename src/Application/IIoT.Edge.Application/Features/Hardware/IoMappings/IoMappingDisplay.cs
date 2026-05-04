namespace IIoT.Edge.Application.Features.Hardware.IoMappings;

/// <summary>
/// IO 映射展示分组规则。硬件配置和 IO 交互必须共用这套分类语义，避免两处页面对同一 PLC 点位显示不一致。
/// </summary>
public static class IoMappingDisplay
{
    public const string InteractionCategory = "信号交互";
    public const string SingleReadCategory = "单点读数据";
    public const string SingleWriteCategory = "单点写数据";
    public const string ContinuousReadCategory = "连续读数据";

    public static string ResolveCategory(string? category, int addressCount)
    {
        if (!string.IsNullOrWhiteSpace(category))
        {
            return category.Trim();
        }

        return addressCount > 1 ? ContinuousReadCategory : SingleReadCategory;
    }

    public static string ResolveGroupName(string? groupName, string category)
        => string.IsNullOrWhiteSpace(groupName)
            ? category
            : groupName.Trim();

    public static bool IsContinuousMatrix(string? dataType, int addressCount)
        => addressCount > 1
            && !string.Equals(dataType, "Ascii", StringComparison.OrdinalIgnoreCase);

    public static string BuildSectionTitle(string? category, string? groupName)
        => ResolveCategory(category, addressCount: 1);
}
