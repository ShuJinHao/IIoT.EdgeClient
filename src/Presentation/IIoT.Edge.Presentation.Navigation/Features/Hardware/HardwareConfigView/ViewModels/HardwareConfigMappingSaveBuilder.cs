using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public interface IHardwareConfigMappingSaveBuilder
{
    IReadOnlyCollection<IoMappingVm> BuildMappingsToSave(IEnumerable<IoMappingVm> ioMappings);
}

public sealed class HardwareConfigMappingSaveBuilder : IHardwareConfigMappingSaveBuilder
{
    private const int ManualSortOrderBase = 10000;

    public IReadOnlyCollection<IoMappingVm> BuildMappingsToSave(IEnumerable<IoMappingVm> ioMappings)
    {
        var mappings = ioMappings.ToArray();
        var result = new List<IoMappingVm>(mappings.Length);

        foreach (var standard in mappings.Where(static x => !IsManualSignal(x)))
        {
            result.Add(CloneIoMapping(standard));
        }

        var manualOrdered = mappings
            .Where(static x => IsManualSignal(x))
            .OrderBy(static x => string.Equals(
                x.Direction,
                IoMappingOptionCatalog.DirectionWrite,
                StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(static x => x.SortOrder <= 0 ? int.MaxValue : x.SortOrder)
            .ThenBy(static x => x.SignalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < manualOrdered.Length; index++)
        {
            var clone = CloneIoMapping(manualOrdered[index]);
            clone.SortOrder = ManualSortOrderBase + index;
            result.Add(clone);
        }

        return result;
    }

    private static bool IsManualSignal(IoMappingVm mapping)
        => mapping.SignalKey?.StartsWith("Manual.", StringComparison.OrdinalIgnoreCase) ?? false;

    private static IoMappingVm CloneIoMapping(IoMappingVm source)
        => new()
        {
            Id = source.Id,
            NetworkDeviceId = source.NetworkDeviceId,
            SignalKey = source.SignalKey,
            PlcAddress = source.PlcAddress,
            Category = source.Category,
            AddressCount = source.AddressCount,
            DataType = source.DataType,
            Direction = source.Direction,
            BusinessGroup = source.BusinessGroup,
            SignalName = source.SignalName,
            SortOrder = source.SortOrder,
            Remark = source.Remark
        };
}
