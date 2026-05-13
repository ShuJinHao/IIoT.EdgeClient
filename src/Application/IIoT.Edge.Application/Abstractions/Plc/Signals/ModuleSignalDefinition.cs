namespace IIoT.Edge.Application.Abstractions.Plc.Signals;

/// <summary>
/// PLC 信号读写方向。
/// </summary>
public enum ModuleSignalDirection
{
    Read = 0,
    Write = 1
}

/// <summary>
/// 插件强类型 PLC 信号定义，作为硬件模板、开发播种和 Runtime 逻辑访问的统一来源。
/// </summary>
/// <typeparam name="TSignalKey">插件声明的 PLC 信号枚举。</typeparam>
public sealed record ModuleSignalDefinition<TSignalKey>(
    TSignalKey Key,
    string SignalKey,
    string DisplayName,
    string DefaultAddress,
    int AddressCount,
    string DataType,
    ModuleSignalDirection Direction,
    int SortOrder,
    string Category,
    string BusinessGroup,
    string SignalName)
    where TSignalKey : struct, Enum
{
    /// <summary>
    /// 方向文本用于落库和硬件配置 UI，保持与现有 IO 映射表一致。
    /// </summary>
    public string DirectionText => Direction == ModuleSignalDirection.Write ? "Write" : "Read";
}

/// <summary>
/// 插件 PLC 信号业务场景分组，用于 profile 内部组织和硬件/IO 页面定位。
/// </summary>
/// <typeparam name="TSignalKey">插件声明的 PLC 信号枚举。</typeparam>
public sealed record ModuleSignalGroup<TSignalKey>(
    string Name,
    IReadOnlyList<ModuleSignalDefinition<TSignalKey>> Signals)
    where TSignalKey : struct, Enum;
