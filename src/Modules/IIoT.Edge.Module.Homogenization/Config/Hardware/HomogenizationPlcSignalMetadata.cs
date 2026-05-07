using System.Reflection;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

/// <summary>
/// 匀浆信号交互元数据。一个枚举成员必须同时声明 PLC->PC 读点和 PC->PLC 写点，保证界面配置和运行任务使用同一套业务动作。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HomogenizationInteractionSignalAttribute : Attribute
{
    /// <summary>
    /// 创建一套匀浆信号交互的默认 IO 模板元数据。
    /// </summary>
    public HomogenizationInteractionSignalAttribute(
        string signalKey,
        string businessGroup,
        string readAddress,
        string writeAddress,
        int readSortOrder,
        int writeSortOrder)
    {
        SignalKey = signalKey;
        BusinessGroup = businessGroup;
        ReadAddress = readAddress;
        WriteAddress = writeAddress;
        ReadSortOrder = readSortOrder;
        WriteSortOrder = writeSortOrder;
    }

    /// <summary>同一业务动作读写两条映射共用的稳定信号键。</summary>
    public string SignalKey { get; }

    /// <summary>界面按业务动作展示和成对删除时使用的业务组名称。</summary>
    public string BusinessGroup { get; }

    /// <summary>PLC 写给上位机的触发读点默认地址。</summary>
    public string ReadAddress { get; }

    /// <summary>上位机写回 PLC 的应答写点默认地址。</summary>
    public string WriteAddress { get; }

    /// <summary>读点在匀浆标准模板中的排序。</summary>
    public int ReadSortOrder { get; }

    /// <summary>写点在匀浆标准模板中的排序。</summary>
    public int WriteSortOrder { get; }

    /// <summary>读写点默认地址长度，信号交互默认一个 word。</summary>
    public int AddressCount { get; init; } = 1;

    /// <summary>读写点默认数据类型，信号交互默认 Int16 握手码。</summary>
    public string DataType { get; init; } = "Int16";

    /// <summary>PLC->PC 读点在表格中的信号名称。</summary>
    public string ReadSignalName { get; init; } = "PLC 触发";

    /// <summary>PC->PLC 写点在表格中的信号名称。</summary>
    public string WriteSignalName { get; init; } = "上位机应答";
}

/// <summary>
/// 匀浆读取类点位元数据。读取长度、数据类型和默认地址属于插件业务定义，不写进宿主默认配置。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HomogenizationReadSignalAttribute : Attribute
{
    /// <summary>
    /// 创建匀浆单点或连续读数据的默认 IO 模板元数据。
    /// </summary>
    public HomogenizationReadSignalAttribute(
        string signalKey,
        string defaultAddress,
        int addressCount,
        string dataType,
        int sortOrder,
        string category,
        string businessGroup,
        string signalName)
    {
        SignalKey = signalKey;
        DefaultAddress = defaultAddress;
        AddressCount = addressCount;
        DataType = dataType;
        SortOrder = sortOrder;
        Category = category;
        BusinessGroup = businessGroup;
        SignalName = signalName;
    }

    /// <summary>插件业务信号键，运行任务和 UI 都按它绑定。</summary>
    public string SignalKey { get; }

    /// <summary>新建或重置标准点位时使用的默认 PLC 地址。</summary>
    public string DefaultAddress { get; }

    /// <summary>一次读取的 PLC 地址长度。</summary>
    public int AddressCount { get; }

    /// <summary>PLC 数据类型。</summary>
    public string DataType { get; }

    /// <summary>标准模板排序。</summary>
    public int SortOrder { get; }

    /// <summary>IO 分类，只允许单点读数据或连续读数据。</summary>
    public string Category { get; }

    /// <summary>界面分组名称。</summary>
    public string BusinessGroup { get; }

    /// <summary>表格展示的信号名称。</summary>
    public string SignalName { get; }

    /// <summary>可选展示名；未配置时使用枚举成员名。</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// 匀浆写入类点位元数据。写数据不进入实时信号交互循环，只给业务任务或手动调试按需写入。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HomogenizationWriteSignalAttribute : Attribute
{
    /// <summary>
    /// 创建匀浆单点或连续写数据的默认 IO 模板元数据。
    /// </summary>
    public HomogenizationWriteSignalAttribute(
        string signalKey,
        string defaultAddress,
        int addressCount,
        string dataType,
        int sortOrder,
        string category,
        string businessGroup,
        string signalName)
    {
        SignalKey = signalKey;
        DefaultAddress = defaultAddress;
        AddressCount = addressCount;
        DataType = dataType;
        SortOrder = sortOrder;
        Category = category;
        BusinessGroup = businessGroup;
        SignalName = signalName;
    }

    /// <summary>插件业务信号键，运行任务和 UI 都按它绑定。</summary>
    public string SignalKey { get; }

    /// <summary>新建或重置标准点位时使用的默认 PLC 地址。</summary>
    public string DefaultAddress { get; }

    /// <summary>一次写入的 PLC 地址长度。</summary>
    public int AddressCount { get; }

    /// <summary>PLC 数据类型。</summary>
    public string DataType { get; }

    /// <summary>标准模板排序。</summary>
    public int SortOrder { get; }

    /// <summary>IO 分类，只允许单点写数据或连续写数据。</summary>
    public string Category { get; }

    /// <summary>界面分组名称。</summary>
    public string BusinessGroup { get; }

    /// <summary>表格展示的信号名称。</summary>
    public string SignalName { get; }

    /// <summary>可选展示名；未配置时使用枚举成员名。</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// 匀浆 profile 读取枚举特性的内部工具。没有特性的枚举成员不会进入标准播种；UI 新增候选由全量枚举另行展开。
/// </summary>
internal static class HomogenizationSignalMetadata
{
    /// <summary>读取信号交互枚举成员上的成对点位元数据。</summary>
    public static InteractionMetadata? TryGetInteractionMetadata(HomogenizationPlcSignals.Interaction signal)
    {
        var attribute = GetFieldAttribute<HomogenizationInteractionSignalAttribute, HomogenizationPlcSignals.Interaction>(signal);
        return attribute is null
            ? null
            : new InteractionMetadata(
                attribute.SignalKey,
                attribute.BusinessGroup,
                attribute.ReadAddress,
                attribute.WriteAddress,
                attribute.ReadSortOrder,
                attribute.WriteSortOrder,
                attribute.AddressCount,
                attribute.DataType,
                attribute.ReadSignalName,
                attribute.WriteSignalName);
    }

    /// <summary>读取单点/连续读数据枚举成员上的点位元数据。</summary>
    public static IoSignalMetadata? TryGetReadMetadata<TSignal>(TSignal signal)
        where TSignal : struct, Enum
    {
        var attribute = GetFieldAttribute<HomogenizationReadSignalAttribute, TSignal>(signal);
        return attribute is null
            ? null
            : new IoSignalMetadata(
                attribute.SignalKey,
                attribute.DefaultAddress,
                attribute.AddressCount,
                attribute.DataType,
                attribute.SortOrder,
                attribute.Category,
                attribute.BusinessGroup,
                attribute.SignalName,
                string.IsNullOrWhiteSpace(attribute.DisplayName) ? signal.ToString() : attribute.DisplayName);
    }

    /// <summary>读取单点/连续写数据枚举成员上的点位元数据。</summary>
    public static IoSignalMetadata? TryGetWriteMetadata<TSignal>(TSignal signal)
        where TSignal : struct, Enum
    {
        var attribute = GetFieldAttribute<HomogenizationWriteSignalAttribute, TSignal>(signal);
        return attribute is null
            ? null
            : new IoSignalMetadata(
                attribute.SignalKey,
                attribute.DefaultAddress,
                attribute.AddressCount,
                attribute.DataType,
                attribute.SortOrder,
                attribute.Category,
                attribute.BusinessGroup,
                attribute.SignalName,
                string.IsNullOrWhiteSpace(attribute.DisplayName) ? signal.ToString() : attribute.DisplayName);
    }

    private static TAttribute? GetFieldAttribute<TAttribute, TSignal>(TSignal signal)
        where TAttribute : Attribute
        where TSignal : struct, Enum
        => typeof(TSignal)
            .GetField(signal.ToString(), BindingFlags.Public | BindingFlags.Static)
            ?.GetCustomAttribute<TAttribute>();
}

/// <summary>
/// profile 内部使用的信号交互展开结果，避免把一套业务动作拆成两套枚举。
/// </summary>
internal sealed record InteractionMetadata(
    string SignalKey,
    string BusinessGroup,
    string ReadAddress,
    string WriteAddress,
    int ReadSortOrder,
    int WriteSortOrder,
    int AddressCount,
    string DataType,
    string ReadSignalName,
    string WriteSignalName);

/// <summary>
/// profile 内部使用的数据点元数据，统一承载单点/连续读写的模板字段。
/// </summary>
internal sealed record IoSignalMetadata(
    string SignalKey,
    string DefaultAddress,
    int AddressCount,
    string DataType,
    int SortOrder,
    string Category,
    string BusinessGroup,
    string SignalName,
    string DisplayName);



