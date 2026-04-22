using IIoT.Edge.Plugin.Shared.Signals;

namespace IIoT.Edge.Module.ScanCaptureStarter.Runtime;

public sealed record StarterSignalDefinition(
    string Label,
    string DisplayName,
    string DefaultAddress,
    int AddressCount,
    string DataType,
    string Direction,
    int SortOrder);

public static class StarterPlcSignalProfile
{
    public const int ScanTriggerReadIndex = 0;
    public const int SequenceReadIndex = 1;
    public const int ResultCodeReadIndex = 2;

    public const int ScanResponseWriteIndex = 0;
    public const int AckWriteIndex = 1;

    public static IReadOnlyList<StarterSignalDefinition> ReadSignals { get; } =
    [
        new("Starter.ScanTrigger", "Scan trigger", "DB1.DBW0", 1, "Int16", "Read", 1),
        new("Starter.Sequence", "Sequence", "DB1.DBW2", 1, "Int16", "Read", 2),
        new("Starter.ResultCode", "Result code", "DB1.DBW4", 1, "Int16", "Read", 3)
    ];

    public static IReadOnlyList<StarterSignalDefinition> WriteSignals { get; } =
    [
        new("Starter.ScanResponse", "Scan response", "DB1.DBW6", 1, "Int16", "Write", 4),
        new("Starter.Ack", "Capture acknowledge", "DB1.DBW8", 1, "Int16", "Write", 5)
    ];

    public static IReadOnlyList<StarterSignalDefinition> Signals { get; } = ReadSignals.Concat(WriteSignals).ToArray();

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

    public static bool? ToCellResult(ushort resultCode)
        => resultCode switch
        {
            1 => true,
            2 => false,
            _ => null
        };
}
