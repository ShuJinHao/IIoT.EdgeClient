using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public sealed class IoInteractionPairVm
{
    public IoInteractionPairVm(IEnumerable<IoMappingVm> mappings)
    {
        var items = mappings
            .OrderBy(static x => x.SortOrder)
            .ThenBy(static x => x.PlcAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ReadMapping = items.FirstOrDefault(static x => string.Equals(
            x.Direction,
            IoMappingOptionCatalog.DirectionRead,
            StringComparison.OrdinalIgnoreCase));
        WriteMapping = items.FirstOrDefault(static x => string.Equals(
            x.Direction,
            IoMappingOptionCatalog.DirectionWrite,
            StringComparison.OrdinalIgnoreCase));
        var first = items.FirstOrDefault();
        BusinessGroup = string.IsNullOrWhiteSpace(first?.BusinessGroup)
            ? first?.SignalKey ?? "--"
            : first.BusinessGroup.Trim();
        SortOrder = items.Length == 0 ? int.MaxValue : items.Min(static x => x.SortOrder);
    }

    public string BusinessGroup { get; }

    public int SortOrder { get; }

    public IoMappingVm? ReadMapping { get; }

    public IoMappingVm? WriteMapping { get; }

    public string ReadPlcAddress => ReadMapping?.PlcAddress ?? "--";

    public int ReadAddressCount => ReadMapping?.AddressCount ?? 0;

    public string ReadDataType => ReadMapping?.DataType ?? "--";

    public string WritePlcAddress => WriteMapping?.PlcAddress ?? "--";

    public int WriteAddressCount => WriteMapping?.AddressCount ?? 0;

    public string WriteDataType => WriteMapping?.DataType ?? "--";

    public string? Remark => ReadMapping?.Remark ?? WriteMapping?.Remark;
}
