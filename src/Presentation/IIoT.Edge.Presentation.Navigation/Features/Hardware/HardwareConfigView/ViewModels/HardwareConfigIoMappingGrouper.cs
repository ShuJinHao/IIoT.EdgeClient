using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// IO 映射分组器，统一维护点位列表到各页面分组集合的转换。
/// </summary>
internal static class HardwareConfigIoMappingGrouper
{
    public static HardwareConfigIoMappingGroupSet Build(IEnumerable<IoMappingVm> mappings)
    {
        var orderedMappings = mappings
            .OrderBy(static x => x.SortOrder)
            .ThenBy(static x => x.PlcAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(
            AllGroups: BuildIoMappingGroups(orderedMappings),
            InteractionPairs: BuildInteractionPairs(orderedMappings),
            InteractionGroups: BuildIoMappingGroups(orderedMappings, IoMappingDisplay.InteractionCategory),
            SingleReadGroups: BuildIoMappingGroups(orderedMappings, IoMappingDisplay.SingleReadCategory),
            ContinuousReadGroups: BuildIoMappingGroups(orderedMappings, IoMappingDisplay.ContinuousReadCategory),
            SingleWriteGroups: BuildIoMappingGroups(orderedMappings, IoMappingDisplay.SingleWriteCategory),
            ContinuousWriteGroups: BuildIoMappingGroups(orderedMappings, IoMappingDisplay.ContinuousWriteCategory));
    }

    private static IoInteractionPairVm[] BuildInteractionPairs(IEnumerable<IoMappingVm> mappings)
        => mappings
            .Where(static x => string.Equals(
                IoMappingDisplay.ResolveCategory(x.Category, x.AddressCount),
                IoMappingDisplay.InteractionCategory,
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(CreateInteractionPairKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new IoInteractionPairVm(group))
            .OrderBy(static x => x.SortOrder)
            .ThenBy(static x => x.BusinessGroup, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string CreateInteractionPairKey(IoMappingVm mapping)
        => string.IsNullOrWhiteSpace(mapping.BusinessGroup)
            ? mapping.SignalKey.Trim()
            : mapping.BusinessGroup.Trim();

    private static IoMappingGroupVm[] BuildIoMappingGroups(IEnumerable<IoMappingVm> mappings, string? category = null)
    {
        var filteredMappings = category is null
            ? mappings
            : mappings.Where(x =>
                string.Equals(
                    IoMappingDisplay.ResolveCategory(x.Category, x.AddressCount),
                    category,
                    StringComparison.OrdinalIgnoreCase));

        return filteredMappings
            .GroupBy(static x => x.GroupTitle, StringComparer.OrdinalIgnoreCase)
            .Select(static x => new IoMappingGroupVm(x.Key, x))
            .ToArray();
    }
}

internal sealed record HardwareConfigIoMappingGroupSet(
    IoMappingGroupVm[] AllGroups,
    IoInteractionPairVm[] InteractionPairs,
    IoMappingGroupVm[] InteractionGroups,
    IoMappingGroupVm[] SingleReadGroups,
    IoMappingGroupVm[] ContinuousReadGroups,
    IoMappingGroupVm[] SingleWriteGroups,
    IoMappingGroupVm[] ContinuousWriteGroups);
