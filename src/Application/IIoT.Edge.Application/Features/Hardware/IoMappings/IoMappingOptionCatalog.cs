namespace IIoT.Edge.Application.Features.Hardware.IoMappings;

/// <summary>
/// IO 映射页面和运行展示共用的选项目录，避免 XAML、ViewModel、测试各自硬编码。
/// </summary>
public static class IoMappingOptionCatalog
{
    public const string CategoryInteraction = "信号交互";
    public const string CategorySingleRead = "单点读数据";
    public const string CategoryContinuousRead = "连续读数据";
    public const string CategorySingleWrite = "单点写数据";
    public const string CategoryContinuousWrite = "连续写数据";

    public const string DirectionRead = "Read";
    public const string DirectionWrite = "Write";

    public const string DataTypeBool = "Bool";
    public const string DataTypeInt16 = "Int16";
    public const string DataTypeUInt16 = "UInt16";
    public const string DataTypeInt32 = "Int32";
    public const string DataTypeFloat = "Float";
    public const string DataTypeAscii = "Ascii";

    public static IReadOnlyList<string> Categories { get; } =
    [
        CategoryInteraction,
        CategorySingleRead,
        CategoryContinuousRead,
        CategorySingleWrite,
        CategoryContinuousWrite
    ];

    public static IReadOnlyList<string> DataPointCategories { get; } =
    [
        CategorySingleRead,
        CategoryContinuousRead,
        CategorySingleWrite,
        CategoryContinuousWrite
    ];

    public static IReadOnlyList<string> Directions { get; } =
    [
        DirectionRead,
        DirectionWrite
    ];

    public static IReadOnlyList<string> DataTypes { get; } =
    [
        DataTypeBool,
        DataTypeInt16,
        DataTypeUInt16,
        DataTypeInt32,
        DataTypeFloat,
        DataTypeAscii
    ];

    public static bool IsKnownCategory(string? value)
        => Contains(Categories, value);

    public static bool IsDataPointCategory(string? value)
        => Contains(DataPointCategories, value);

    public static bool IsInteractionCategory(string? value)
        => string.Equals(NormalizeCategory(value, addressCount: 1), CategoryInteraction, StringComparison.OrdinalIgnoreCase);

    public static bool IsReadDataCategory(string? value)
        => string.Equals(NormalizeCategory(value, addressCount: 1), CategorySingleRead, StringComparison.OrdinalIgnoreCase)
           || string.Equals(NormalizeCategory(value, addressCount: 2), CategoryContinuousRead, StringComparison.OrdinalIgnoreCase);

    public static bool IsWriteDataCategory(string? value)
        => string.Equals(NormalizeCategory(value, addressCount: 1), CategorySingleWrite, StringComparison.OrdinalIgnoreCase)
           || string.Equals(NormalizeCategory(value, addressCount: 2), CategoryContinuousWrite, StringComparison.OrdinalIgnoreCase);

    public static bool IsFixedAddressCountCategory(string? value)
        => false;

    public static bool CanEditAddressCount(string? value)
        => true;

    public static int NormalizeAddressCount(string? category, int addressCount)
        => Math.Max(1, addressCount);

    public static string? GetDirectionForCategory(string? category)
    {
        var normalized = NormalizeCategory(category, addressCount: 1);
        if (string.Equals(normalized, CategorySingleRead, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, CategoryContinuousRead, StringComparison.OrdinalIgnoreCase))
        {
            return DirectionRead;
        }

        if (string.Equals(normalized, CategorySingleWrite, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, CategoryContinuousWrite, StringComparison.OrdinalIgnoreCase))
        {
            return DirectionWrite;
        }

        return null;
    }

    public static bool IsKnownDirection(string? value)
        => Contains(Directions, value);

    public static bool IsKnownDataType(string? value)
        => Contains(DataTypes, value);

    public static int CategoryOrder(string? category)
    {
        var normalized = NormalizeCategory(category, addressCount: 1);
        var index = Categories
            .Select((value, i) => new { value, i })
            .FirstOrDefault(x => string.Equals(x.value, normalized, StringComparison.OrdinalIgnoreCase))
            ?.i;

        return index ?? int.MaxValue;
    }

    public static string NormalizeCategory(string? category, int addressCount)
    {
        if (!string.IsNullOrWhiteSpace(category))
        {
            var trimmed = category.Trim();
            return Categories.FirstOrDefault(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase))
                   ?? trimmed;
        }

        return addressCount > 1 ? CategoryContinuousRead : CategorySingleRead;
    }

    private static bool Contains(IEnumerable<string> values, string? value)
        => !string.IsNullOrWhiteSpace(value)
           && values.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}
