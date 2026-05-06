namespace IIoT.Edge.Application.Features.Hardware.IoMappings;

/// <summary>
/// IO 映射页面和运行展示共用的选项目录，避免 XAML、ViewModel、测试各自硬编码。
/// </summary>
public static class IoMappingOptionCatalog
{
    public const string PointSourceStandardSignal = "插件标准信号";
    public const string PointSourceCustomDebug = "自定义调试点";

    public const string CategoryInteraction = "信号交互";
    public const string CategorySingleRead = "单点读数据";
    public const string CategoryContinuousRead = "连续读数据";

    public const string DirectionRead = "Read";
    public const string DirectionWrite = "Write";

    public const string DataTypeBool = "Bool";
    public const string DataTypeInt16 = "Int16";
    public const string DataTypeUInt16 = "UInt16";
    public const string DataTypeInt32 = "Int32";
    public const string DataTypeFloat = "Float";
    public const string DataTypeAscii = "Ascii";

    public static IReadOnlyList<string> PointSources { get; } =
    [
        PointSourceStandardSignal,
        PointSourceCustomDebug
    ];

    public static IReadOnlyList<string> Categories { get; } =
    [
        CategoryInteraction,
        CategorySingleRead,
        CategoryContinuousRead
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

    public static bool IsKnownDirection(string? value)
        => Contains(Directions, value);

    public static bool IsKnownDataType(string? value)
        => Contains(DataTypes, value);

    public static bool IsKnownPointSource(string? value)
        => Contains(PointSources, value);

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
            return category.Trim();
        }

        return addressCount > 1 ? CategoryContinuousRead : CategorySingleRead;
    }

    private static bool Contains(IEnumerable<string> values, string? value)
        => !string.IsNullOrWhiteSpace(value)
           && values.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}
