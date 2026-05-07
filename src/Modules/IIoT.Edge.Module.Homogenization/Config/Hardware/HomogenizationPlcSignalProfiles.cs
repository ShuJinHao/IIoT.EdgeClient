using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

/// <summary>
/// 匀浆信号交互 profile。一个枚举成员展开为 PLC->PC 读点和 PC->PLC 写点两条物理映射。
/// </summary>
public sealed class HomogenizationInteractionSignalProfile
    : ModulePlcSignalProfileBase<HomogenizationPlcSignals.Interaction>
{
    /// <summary>匀浆信号交互 profile 所属模块。</summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IEnumerable<ModuleSignalGroup<HomogenizationPlcSignals.Interaction>> BuildGroups()
    {
        var signals = Enum.GetValues<HomogenizationPlcSignals.Interaction>()
            .Select(BuildInteractionPairOrNull)
            .Where(static pair => pair is not null)
            .SelectMany(static pair => pair!)
            .OrderBy(static signal => signal.SortOrder)
            .ToArray();

        if (signals.Length > 0)
        {
            yield return Group("信号交互", signals);
        }
    }

    private IReadOnlyList<ModuleSignalDefinition<HomogenizationPlcSignals.Interaction>>? BuildInteractionPairOrNull(
        HomogenizationPlcSignals.Interaction signal)
    {
        var metadata = HomogenizationSignalMetadata.TryGetInteractionMetadata(signal);
        if (metadata is null)
        {
            return null;
        }

        var addressCount = IoMappingOptionCatalog.NormalizeAddressCount(
            IoMappingOptionCatalog.CategoryInteraction,
            metadata.AddressCount);

        return
        [
            Signal(
                signal,
                metadata.SignalKey,
                metadata.ReadAddress,
                ModuleSignalDirection.Read,
                addressCount,
                metadata.DataType,
                metadata.ReadSortOrder,
                $"{metadata.BusinessGroup} PLC 读点",
                IoMappingOptionCatalog.CategoryInteraction,
                metadata.BusinessGroup,
                metadata.ReadSignalName),
            Signal(
                signal,
                metadata.SignalKey,
                metadata.WriteAddress,
                ModuleSignalDirection.Write,
                addressCount,
                metadata.DataType,
                metadata.WriteSortOrder,
                $"{metadata.BusinessGroup} 上位机写点",
                IoMappingOptionCatalog.CategoryInteraction,
                metadata.BusinessGroup,
                metadata.WriteSignalName)
        ];
    }
}

/// <summary>
/// 匀浆单点读数据 profile。只展开带 <see cref="HomogenizationReadSignalAttribute"/> 的枚举成员。
/// </summary>
public sealed class HomogenizationSingleReadSignalProfile
    : ModulePlcSignalProfileBase<HomogenizationPlcSignals.SingleRead>
{
    /// <summary>匀浆单点读数据 profile 所属模块。</summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IEnumerable<ModuleSignalGroup<HomogenizationPlcSignals.SingleRead>> BuildGroups()
        => Enum.GetValues<HomogenizationPlcSignals.SingleRead>()
            .Select(BuildSignalOrNull)
            .Where(static signal => signal is not null)
            .Select(static signal => signal!)
            .GroupBy(static signal => signal.BusinessGroup)
            .Select(static group => new ModuleSignalGroup<HomogenizationPlcSignals.SingleRead>(
                group.Key,
                group.OrderBy(static signal => signal.SortOrder).ToArray()));

    private ModuleSignalDefinition<HomogenizationPlcSignals.SingleRead>? BuildSignalOrNull(HomogenizationPlcSignals.SingleRead signal)
    {
        var metadata = HomogenizationSignalMetadata.TryGetReadMetadata(signal);
        if (metadata is null)
        {
            return null;
        }

        return Signal(
            signal,
            metadata.SignalKey,
            metadata.DefaultAddress,
            ModuleSignalDirection.Read,
            IoMappingOptionCatalog.NormalizeAddressCount(metadata.Category, metadata.AddressCount),
            metadata.DataType,
            metadata.SortOrder,
            metadata.DisplayName,
            metadata.Category,
            metadata.BusinessGroup,
            metadata.SignalName);
    }
}

/// <summary>
/// 匀浆连续读数据 profile。只展开带 <see cref="HomogenizationReadSignalAttribute"/> 的枚举成员。
/// </summary>
public sealed class HomogenizationContinuousReadSignalProfile
    : ModulePlcSignalProfileBase<HomogenizationPlcSignals.ContinuousRead>
{
    /// <summary>匀浆连续读数据 profile 所属模块。</summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IEnumerable<ModuleSignalGroup<HomogenizationPlcSignals.ContinuousRead>> BuildGroups()
        => Enum.GetValues<HomogenizationPlcSignals.ContinuousRead>()
            .Select(BuildSignalOrNull)
            .Where(static signal => signal is not null)
            .Select(static signal => signal!)
            .GroupBy(static signal => signal.BusinessGroup)
            .Select(static group => new ModuleSignalGroup<HomogenizationPlcSignals.ContinuousRead>(
                group.Key,
                group.OrderBy(static signal => signal.SortOrder).ToArray()));

    private ModuleSignalDefinition<HomogenizationPlcSignals.ContinuousRead>? BuildSignalOrNull(HomogenizationPlcSignals.ContinuousRead signal)
    {
        var metadata = HomogenizationSignalMetadata.TryGetReadMetadata(signal);
        if (metadata is null)
        {
            return null;
        }

        return Signal(
            signal,
            metadata.SignalKey,
            metadata.DefaultAddress,
            ModuleSignalDirection.Read,
            IoMappingOptionCatalog.NormalizeAddressCount(metadata.Category, metadata.AddressCount),
            metadata.DataType,
            metadata.SortOrder,
            metadata.DisplayName,
            metadata.Category,
            metadata.BusinessGroup,
            metadata.SignalName);
    }
}

/// <summary>
/// 匀浆单点写数据 profile。只展开带 <see cref="HomogenizationWriteSignalAttribute"/> 的枚举成员。
/// </summary>
public sealed class HomogenizationSingleWriteSignalProfile
    : ModulePlcSignalProfileBase<HomogenizationPlcSignals.SingleWrite>
{
    /// <summary>匀浆单点写数据 profile 所属模块。</summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IEnumerable<ModuleSignalGroup<HomogenizationPlcSignals.SingleWrite>> BuildGroups()
        => Enum.GetValues<HomogenizationPlcSignals.SingleWrite>()
            .Select(BuildSignalOrNull)
            .Where(static signal => signal is not null)
            .Select(static signal => signal!)
            .GroupBy(static signal => signal.BusinessGroup)
            .Select(static group => new ModuleSignalGroup<HomogenizationPlcSignals.SingleWrite>(
                group.Key,
                group.OrderBy(static signal => signal.SortOrder).ToArray()));

    private ModuleSignalDefinition<HomogenizationPlcSignals.SingleWrite>? BuildSignalOrNull(HomogenizationPlcSignals.SingleWrite signal)
    {
        var metadata = HomogenizationSignalMetadata.TryGetWriteMetadata(signal);
        if (metadata is null)
        {
            return null;
        }

        return Signal(
            signal,
            metadata.SignalKey,
            metadata.DefaultAddress,
            ModuleSignalDirection.Write,
            IoMappingOptionCatalog.NormalizeAddressCount(metadata.Category, metadata.AddressCount),
            metadata.DataType,
            metadata.SortOrder,
            metadata.DisplayName,
            metadata.Category,
            metadata.BusinessGroup,
            metadata.SignalName);
    }
}

/// <summary>
/// 匀浆连续写数据 profile。只展开带 <see cref="HomogenizationWriteSignalAttribute"/> 的枚举成员。
/// </summary>
public sealed class HomogenizationContinuousWriteSignalProfile
    : ModulePlcSignalProfileBase<HomogenizationPlcSignals.ContinuousWrite>
{
    /// <summary>匀浆连续写数据 profile 所属模块。</summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IEnumerable<ModuleSignalGroup<HomogenizationPlcSignals.ContinuousWrite>> BuildGroups()
        => Enum.GetValues<HomogenizationPlcSignals.ContinuousWrite>()
            .Select(BuildSignalOrNull)
            .Where(static signal => signal is not null)
            .Select(static signal => signal!)
            .GroupBy(static signal => signal.BusinessGroup)
            .Select(static group => new ModuleSignalGroup<HomogenizationPlcSignals.ContinuousWrite>(
                group.Key,
                group.OrderBy(static signal => signal.SortOrder).ToArray()));

    private ModuleSignalDefinition<HomogenizationPlcSignals.ContinuousWrite>? BuildSignalOrNull(HomogenizationPlcSignals.ContinuousWrite signal)
    {
        var metadata = HomogenizationSignalMetadata.TryGetWriteMetadata(signal);
        if (metadata is null)
        {
            return null;
        }

        return Signal(
            signal,
            metadata.SignalKey,
            metadata.DefaultAddress,
            ModuleSignalDirection.Write,
            IoMappingOptionCatalog.NormalizeAddressCount(metadata.Category, metadata.AddressCount),
            metadata.DataType,
            metadata.SortOrder,
            metadata.DisplayName,
            metadata.Category,
            metadata.BusinessGroup,
            metadata.SignalName);
    }
}

