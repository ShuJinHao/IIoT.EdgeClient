using IIoT.Edge.Application.Features.Hardware.IoMappings;

namespace IIoT.Edge.Application.Modules.Hardware;

/// <summary>
/// 声明 PLC 信号交互点位。一项业务交互会展开为一条读映射和一条写映射。
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class PlcInteractionSignalAttribute(
    string signalKey,
    string businessGroup,
    string readAddress,
    string writeAddress,
    int readSortOrder,
    int writeSortOrder) : Attribute
{
    public string SignalKey { get; } = signalKey;

    public string BusinessGroup { get; } = businessGroup;

    public string ReadAddress { get; } = readAddress;

    public string WriteAddress { get; } = writeAddress;

    public int ReadSortOrder { get; } = readSortOrder;

    public int WriteSortOrder { get; } = writeSortOrder;

    public int AddressCount { get; init; } = 1;

    public string DataType { get; init; } = IoMappingOptionCatalog.DataTypeInt16;
}

/// <summary>
/// 声明 PLC 读点位，适用于单点读和连续读。
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class PlcReadSignalAttribute(
    string signalKey,
    string defaultAddress,
    int addressCount,
    string dataType,
    int sortOrder,
    string category,
    string businessGroup,
    string displayName) : PlcIoSignalAttribute(
        signalKey,
        defaultAddress,
        addressCount,
        dataType,
        sortOrder,
        category,
        businessGroup,
        displayName);

/// <summary>
/// 声明 PLC 写点位，适用于单点写和连续写。
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class PlcWriteSignalAttribute(
    string signalKey,
    string defaultAddress,
    int addressCount,
    string dataType,
    int sortOrder,
    string category,
    string businessGroup,
    string displayName) : PlcIoSignalAttribute(
        signalKey,
        defaultAddress,
        addressCount,
        dataType,
        sortOrder,
        category,
        businessGroup,
        displayName);

/// <summary>
/// 声明 PLC IO 点位的公共元数据。读写方向由使用的具体特性类型决定。
/// </summary>
public abstract class PlcIoSignalAttribute(
    string signalKey,
    string defaultAddress,
    int addressCount,
    string dataType,
    int sortOrder,
    string category,
    string businessGroup,
    string displayName) : Attribute
{
    public string SignalKey { get; } = signalKey;

    public string DefaultAddress { get; } = defaultAddress;

    public int AddressCount { get; } = addressCount;

    public string DataType { get; } = dataType;

    public int SortOrder { get; } = sortOrder;

    public string Category { get; } = category;

    public string BusinessGroup { get; } = businessGroup;

    public string DisplayName { get; init; } = displayName;
}
