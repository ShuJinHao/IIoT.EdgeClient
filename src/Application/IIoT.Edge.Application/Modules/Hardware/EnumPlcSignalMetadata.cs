using System.Reflection;

namespace IIoT.Edge.Application.Modules.Hardware;

public sealed record PlcInteractionSignalMetadata(
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

public sealed record PlcIoSignalMetadata(
    string SignalKey,
    string DefaultAddress,
    int AddressCount,
    string DataType,
    int SortOrder,
    string Category,
    string BusinessGroup,
    string SignalName,
    string? DisplayName);

public static class EnumPlcSignalMetadata
{
    public static PlcInteractionSignalMetadata GetInteraction<TSignalKey>(TSignalKey signal)
        where TSignalKey : struct, Enum
        => TryGetInteraction(signal)
            ?? throw MissingMetadata<TSignalKey>(signal, nameof(PlcInteractionSignalAttribute));

    public static PlcInteractionSignalMetadata? TryGetInteraction<TSignalKey>(TSignalKey signal)
        where TSignalKey : struct, Enum
        => GetField(signal)?.GetCustomAttribute<PlcInteractionSignalAttribute>() is { } attribute
            ? new PlcInteractionSignalMetadata(
                attribute.SignalKey,
                attribute.BusinessGroup,
                attribute.ReadAddress,
                attribute.WriteAddress,
                attribute.ReadSortOrder,
                attribute.WriteSortOrder,
                attribute.AddressCount,
                attribute.DataType,
                attribute.ReadSignalName,
                attribute.WriteSignalName)
            : null;

    public static PlcIoSignalMetadata GetRead<TSignalKey>(TSignalKey signal)
        where TSignalKey : struct, Enum
        => TryGetRead(signal)
            ?? throw MissingMetadata<TSignalKey>(signal, nameof(PlcReadSignalAttribute));

    public static PlcIoSignalMetadata? TryGetRead<TSignalKey>(TSignalKey signal)
        where TSignalKey : struct, Enum
        => GetField(signal)?.GetCustomAttribute<PlcReadSignalAttribute>() is { } attribute
            ? new PlcIoSignalMetadata(
                attribute.SignalKey,
                attribute.DefaultAddress,
                attribute.AddressCount,
                attribute.DataType,
                attribute.SortOrder,
                attribute.Category,
                attribute.BusinessGroup,
                attribute.SignalName,
                attribute.DisplayName)
            : null;

    public static PlcIoSignalMetadata GetWrite<TSignalKey>(TSignalKey signal)
        where TSignalKey : struct, Enum
        => TryGetWrite(signal)
            ?? throw MissingMetadata<TSignalKey>(signal, nameof(PlcWriteSignalAttribute));

    public static PlcIoSignalMetadata? TryGetWrite<TSignalKey>(TSignalKey signal)
        where TSignalKey : struct, Enum
        => GetField(signal)?.GetCustomAttribute<PlcWriteSignalAttribute>() is { } attribute
            ? new PlcIoSignalMetadata(
                attribute.SignalKey,
                attribute.DefaultAddress,
                attribute.AddressCount,
                attribute.DataType,
                attribute.SortOrder,
                attribute.Category,
                attribute.BusinessGroup,
                attribute.SignalName,
                attribute.DisplayName)
            : null;

    private static FieldInfo? GetField<TSignalKey>(TSignalKey signal)
        where TSignalKey : struct, Enum
        => typeof(TSignalKey).GetField(signal.ToString());

    private static InvalidOperationException MissingMetadata<TSignalKey>(TSignalKey signal, string attributeName)
        where TSignalKey : struct, Enum
        => new($"PLC 信号枚举【{typeof(TSignalKey).FullName}.{signal}】缺少 {attributeName} 声明。");
}
