using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Features.Hardware.IoMappings;

namespace IIoT.Edge.Application.Modules.Hardware;

/// <summary>
/// 基于枚举特性的标准信号交互 profile。
/// </summary>
public sealed class EnumInteractionSignalProfile<TSignalKey>(string moduleId)
    : ModulePlcSignalProfileBase<TSignalKey>
    where TSignalKey : struct, Enum
{
    public override string ModuleId { get; } = moduleId;

    protected override IEnumerable<ModuleSignalGroup<TSignalKey>> BuildGroups()
        => [Group("信号交互", [.. Enum.GetValues<TSignalKey>().SelectMany(BuildSignals)])];

    private IEnumerable<ModuleSignalDefinition<TSignalKey>> BuildSignals(TSignalKey signal)
    {
        var metadata = EnumPlcSignalMetadata.GetInteraction(signal);
        var addressCount = IoMappingOptionCatalog.NormalizeAddressCount(
            IoMappingOptionCatalog.CategoryInteraction,
            metadata.AddressCount);

        yield return Signal(
            signal,
            metadata.SignalKey,
            metadata.ReadAddress,
            ModuleSignalDirection.Read,
            addressCount,
            metadata.DataType,
            metadata.ReadSortOrder,
            $"{metadata.BusinessGroup} PLC 读点",
            IoMappingOptionCatalog.CategoryInteraction,
            metadata.BusinessGroup);

        yield return Signal(
            signal,
            metadata.SignalKey,
            metadata.WriteAddress,
            ModuleSignalDirection.Write,
            addressCount,
            metadata.DataType,
            metadata.WriteSortOrder,
            $"{metadata.BusinessGroup} 上位机写点",
            IoMappingOptionCatalog.CategoryInteraction,
            metadata.BusinessGroup);
    }
}

/// <summary>
/// 基于枚举特性的标准读 profile。
/// </summary>
public sealed class EnumReadSignalProfile<TSignalKey>(string moduleId, string category)
    : EnumIoSignalProfile<TSignalKey, PlcReadSignalAttribute>(moduleId, category, ModuleSignalDirection.Read)
    where TSignalKey : struct, Enum
{
}

/// <summary>
/// 基于枚举特性的标准写 profile。
/// </summary>
public sealed class EnumWriteSignalProfile<TSignalKey>(string moduleId, string category)
    : EnumIoSignalProfile<TSignalKey, PlcWriteSignalAttribute>(moduleId, category, ModuleSignalDirection.Write)
    where TSignalKey : struct, Enum
{
}

/// <summary>
/// 基于枚举特性的标准 IO profile 模板，读写只通过特性类型和方向区分。
/// </summary>
public abstract class EnumIoSignalProfile<TSignalKey, TAttribute>(
    string moduleId,
    string category,
    ModuleSignalDirection direction)
    : ModulePlcSignalProfileBase<TSignalKey>
    where TSignalKey : struct, Enum
    where TAttribute : PlcIoSignalAttribute
{
    public override string ModuleId { get; } = moduleId;

    protected override IEnumerable<ModuleSignalGroup<TSignalKey>> BuildGroups()
        => Enum.GetValues<TSignalKey>()
            .Select(BuildSignal)
            .GroupBy(static signal => signal.BusinessGroup, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Min(static signal => signal.SortOrder))
            .Select(group => Group(group.Key, [.. group.OrderBy(static signal => signal.SortOrder)]));

    private ModuleSignalDefinition<TSignalKey> BuildSignal(TSignalKey signal)
    {
        var metadata = EnumPlcSignalMetadata.GetIo<TSignalKey, TAttribute>(signal);
        var effectiveCategory = string.IsNullOrWhiteSpace(metadata.Category) ? category : metadata.Category;

        return Signal(
            signal,
            metadata.SignalKey,
            metadata.DefaultAddress,
            direction,
            IoMappingOptionCatalog.NormalizeAddressCount(effectiveCategory, metadata.AddressCount),
            metadata.DataType,
            metadata.SortOrder,
            metadata.DisplayName,
            effectiveCategory,
            metadata.BusinessGroup);
    }
}
