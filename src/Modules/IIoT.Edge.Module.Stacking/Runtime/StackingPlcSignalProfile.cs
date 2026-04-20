using IIoT.Edge.Module.Stacking.Constants;

namespace IIoT.Edge.Module.Stacking.Runtime;

public sealed record StackingSignalDefinition(
    string Label,
    string Direction,
    string DefaultAddress,
    int AddressCount,
    string DataType,
    int SortOrder,
    string DisplayName);

public static class StackingPlcSignalProfile
{
    public static readonly StackingSignalDefinition Sequence = new(
        "Stacking.Sequence",
        "Read",
        "DB1.DBW0",
        1,
        "Int16",
        1,
        "工序序号");

    public static readonly StackingSignalDefinition LayerCount = new(
        "Stacking.LayerCount",
        "Read",
        "DB1.DBW2",
        1,
        "Int16",
        2,
        "叠片层数");

    public static readonly StackingSignalDefinition ResultCode = new(
        "Stacking.ResultCode",
        "Read",
        "DB1.DBW4",
        1,
        "Int16",
        3,
        "结果码");

    public static readonly StackingSignalDefinition Ack = new(
        "Stacking.Ack",
        "Write",
        "DB1.DBW6",
        1,
        "Int16",
        4,
        "采集应答");

    public static IReadOnlyList<StackingSignalDefinition> Signals { get; } =
    [
        Sequence,
        LayerCount,
        ResultCode,
        Ack
    ];

    public static IReadOnlyList<StackingSignalDefinition> ReadSignals { get; } =
        Signals.Where(static x => x.Direction == "Read")
            .OrderBy(static x => x.SortOrder)
            .ToArray();

    public static IReadOnlyList<StackingSignalDefinition> WriteSignals { get; } =
        Signals.Where(static x => x.Direction == "Write")
            .OrderBy(static x => x.SortOrder)
            .ToArray();

    public static int SequenceReadIndex => 0;

    public static int LayerCountReadIndex => 1;

    public static int ResultCodeReadIndex => 2;

    public static int AckWriteIndex => 0;

    public static StackingResultCode ParseResultCode(ushort rawValue)
        => Enum.IsDefined(typeof(StackingResultCode), (int)rawValue)
            ? (StackingResultCode)rawValue
            : StackingResultCode.Unknown;

    public static bool? ToCellResult(StackingResultCode resultCode)
        => resultCode switch
        {
            StackingResultCode.Ok => true,
            StackingResultCode.Ng => false,
            _ => null
        };
}
