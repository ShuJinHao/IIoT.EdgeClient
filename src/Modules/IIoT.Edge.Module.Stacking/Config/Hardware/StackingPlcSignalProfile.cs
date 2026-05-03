using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Stacking.Constants;

namespace IIoT.Edge.Module.Stacking.Config.Hardware;

/// <summary>
/// 叠片 PLC 信号定义，描述默认地址、方向、类型和硬件配置页展示名。
/// </summary>
public sealed record StackingSignalDefinition(
    string Label,
    string Direction,
    string DefaultAddress,
    int AddressCount,
    string DataType,
    int SortOrder,
    string DisplayName) : IModulePlcSignalDefinition
{
    /// <summary>
    /// 叠片样本信号统一展示在单点读数据分类。
    /// </summary>
    public string Category => "单点读数据";

    /// <summary>
    /// 叠片样本暂不区分分组。
    /// </summary>
    public string GroupName => string.Empty;

    /// <summary>
    /// 叠片样本暂不区分显示角色。
    /// </summary>
    public string DisplayRole => string.Empty;
}

/// <summary>
/// 叠片 PLC 信号清单和信号值转换规则，运行任务和硬件模板都以这里为准。
/// </summary>
public static class StackingPlcSignalProfile
{
    /// <summary>
    /// PLC 工序序号，序号递增时生成新的叠片记录。
    /// </summary>
    public static readonly StackingSignalDefinition Sequence = new(
        "Stacking.Sequence",
        "Read",
        "DB1.DBW0",
        1,
        "Int16",
        1,
        "工序序号");

    /// <summary>
    /// PLC 叠片层数信号。
    /// </summary>
    public static readonly StackingSignalDefinition LayerCount = new(
        "Stacking.LayerCount",
        "Read",
        "DB1.DBW2",
        1,
        "Int16",
        2,
        "叠片层数");

    /// <summary>
    /// PLC 结果码信号，转换为 StackingResultCode 后决定 CellResult。
    /// </summary>
    public static readonly StackingSignalDefinition ResultCode = new(
        "Stacking.ResultCode",
        "Read",
        "DB1.DBW4",
        1,
        "Int16",
        3,
        "结果码");

    /// <summary>
    /// 上位机应答信号，采集成功后写回当前序号。
    /// </summary>
    public static readonly StackingSignalDefinition Ack = new(
        "Stacking.Ack",
        "Write",
        "DB1.DBW6",
        1,
        "Int16",
        4,
        "采集应答");

    /// <summary>
    /// 叠片模块完整信号清单，插件业务信号不放入共享层。
    /// </summary>
    public static IReadOnlyList<StackingSignalDefinition> Signals { get; } =
    [
        Sequence,
        LayerCount,
        ResultCode,
        Ack
    ];

    /// <summary>
    /// 按排序号排列的读信号。
    /// </summary>
    public static IReadOnlyList<StackingSignalDefinition> ReadSignals { get; } =
        Signals.Where(static x => x.Direction == "Read")
            .OrderBy(static x => x.SortOrder)
            .ToArray();

    /// <summary>
    /// 按排序号排列的写信号。
    /// </summary>
    public static IReadOnlyList<StackingSignalDefinition> WriteSignals { get; } =
        Signals.Where(static x => x.Direction == "Write")
            .OrderBy(static x => x.SortOrder)
            .ToArray();

    /// <summary>
    /// 运行时按 label 访问 PLC 缓冲区所需的逻辑信号清单。
    /// </summary>
    public static IReadOnlyList<ModuleSignalDefinition> LogicalSignals { get; } =
        Signals.Select(static signal => new ModuleSignalDefinition(
                signal.Label,
                signal.DisplayName,
                signal.DefaultAddress,
                signal.AddressCount,
                signal.DataType,
                string.Equals(signal.Direction, "Write", StringComparison.OrdinalIgnoreCase)
                    ? ModuleSignalDirection.Write
                    : ModuleSignalDirection.Read,
                signal.SortOrder))
            .ToArray();

    /// <summary>
    /// 兼容旧固定索引测试的工序序号读索引。
    /// </summary>
    public static int SequenceReadIndex => 0;

    /// <summary>
    /// 兼容旧固定索引测试的层数读索引。
    /// </summary>
    public static int LayerCountReadIndex => 1;

    /// <summary>
    /// 兼容旧固定索引测试的结果码读索引。
    /// </summary>
    public static int ResultCodeReadIndex => 2;

    /// <summary>
    /// 兼容旧固定索引测试的应答写索引。
    /// </summary>
    public static int AckWriteIndex => 0;

    /// <summary>
    /// 将 PLC 原始结果码转换为叠片业务结果码，未知值不抛异常。
    /// </summary>
    public static StackingResultCode ParseResultCode(ushort rawValue)
        => Enum.IsDefined(typeof(StackingResultCode), (int)rawValue)
            ? (StackingResultCode)rawValue
            : StackingResultCode.Unknown;

    /// <summary>
    /// 将叠片业务结果码转换为电芯结果，未知值保持 null 以便云端/诊断区分未判定。
    /// </summary>
    public static bool? ToCellResult(StackingResultCode resultCode)
        => resultCode switch
        {
            StackingResultCode.Ok => true,
            StackingResultCode.Ng => false,
            _ => null
        };
}
