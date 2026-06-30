using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public interface IIoViewMappingBuilder
{
    IoViewMappingBuildResult Build(IEnumerable<IoMappingEntity> mappings);
}

internal sealed class IoViewMappingBuilder : IIoViewMappingBuilder
{
    public IoViewMappingBuildResult Build(IEnumerable<IoMappingEntity> mappings)
    {
        var readIndex = 0;
        var writeIndex = 0;
        var interactionRows = new Dictionary<string, IoInteractionRowModel>(StringComparer.OrdinalIgnoreCase);
        var singleReadSections = new Dictionary<string, IoDataSectionModel>(StringComparer.OrdinalIgnoreCase);
        var continuousReadSections = new Dictionary<string, IoDataSectionModel>(StringComparer.OrdinalIgnoreCase);
        var singleWriteSections = new Dictionary<string, IoDataSectionModel>(StringComparer.OrdinalIgnoreCase);
        var continuousWriteSections = new Dictionary<string, IoDataSectionModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings.OrderBy(static x => x.SortOrder))
        {
            var isRead = string.Equals(mapping.Direction, IoMappingOptionCatalog.DirectionRead, StringComparison.OrdinalIgnoreCase);
            var signal = CreateSignal(mapping, isRead ? readIndex : writeIndex);

            if (isRead)
            {
                readIndex += Math.Max(1, mapping.AddressCount);
            }
            else
            {
                writeIndex += Math.Max(1, mapping.AddressCount);
            }

            var category = ResolveCategory(mapping);
            if (string.Equals(category, IoMappingDisplay.InteractionCategory, StringComparison.OrdinalIgnoreCase))
            {
                var row = GetOrCreateInteractionRow(interactionRows, mapping);
                row.SortOrder = Math.Min(row.SortOrder, mapping.SortOrder);

                if (isRead)
                {
                    row.AddPlcSignal(signal);
                }
                else
                {
                    row.AddHostSignal(signal);
                }

                continue;
            }

            var sections = ResolveSectionBucket(
                category,
                signal.Direction,
                signal.AddressCount,
                singleReadSections,
                continuousReadSections,
                singleWriteSections,
                continuousWriteSections);
            var section = GetOrCreateDataSection(sections, mapping, category);
            section.SortOrder = Math.Min(section.SortOrder, mapping.SortOrder);
            section.Signals.Add(signal);
        }

        return new IoViewMappingBuildResult(
            SortRows(interactionRows.Values),
            SortDataSections(singleReadSections.Values),
            SortDataSections(continuousReadSections.Values),
            SortDataSections(singleWriteSections.Values),
            SortDataSections(continuousWriteSections.Values));
    }

    private static IReadOnlyList<IoInteractionRowModel> SortRows(IEnumerable<IoInteractionRowModel> rows)
        => rows
            .OrderBy(static x => x.SortOrder)
            .ThenBy(static x => x.BusinessGroup, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<IoDataSectionModel> SortDataSections(IEnumerable<IoDataSectionModel> sections)
        => sections
            .OrderBy(static x => x.SortOrder)
            .ThenBy(static x => x.BusinessGroup, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IoInteractionRowModel GetOrCreateInteractionRow(
        IDictionary<string, IoInteractionRowModel> rows,
        IoMappingEntity mapping)
    {
        var businessGroup = ResolveBusinessGroup(mapping, IoMappingDisplay.InteractionCategory);
        if (rows.TryGetValue(businessGroup, out var row))
        {
            return row;
        }

        row = new IoInteractionRowModel
        {
            BusinessGroup = businessGroup,
            SortOrder = mapping.SortOrder
        };
        rows.Add(businessGroup, row);
        return row;
    }

    private static IoDataSectionModel GetOrCreateDataSection(
        IDictionary<string, IoDataSectionModel> sections,
        IoMappingEntity mapping,
        string category)
    {
        var businessGroup = ResolveBusinessGroup(mapping, category);
        var key = category;
        if (sections.TryGetValue(key, out var section))
        {
            return section;
        }

        section = new IoDataSectionModel
        {
            Category = category,
            BusinessGroup = businessGroup,
            SortOrder = mapping.SortOrder
        };
        sections.Add(key, section);
        return section;
    }

    private static IoSignalModel CreateSignal(IoMappingEntity mapping, int startIndex)
    {
        return new IoSignalModel
        {
            SignalKey = mapping.SignalKey,
            PlcAddress = mapping.PlcAddress,
            Direction = mapping.Direction,
            Remark = mapping.Remark,
            DataType = mapping.DataType,
            StartIndex = startIndex,
            AddressCount = Math.Max(1, mapping.AddressCount),
            SortOrder = mapping.SortOrder
        };
    }

    private static string ResolveCategory(IoMappingEntity mapping)
    {
        var category = IoMappingDisplay.ResolveCategory(mapping.Category, mapping.AddressCount);
        if (IoMappingOptionCatalog.IsKnownCategory(category))
        {
            return category;
        }

        if (string.Equals(mapping.Direction, IoMappingOptionCatalog.DirectionWrite, StringComparison.OrdinalIgnoreCase))
        {
            return mapping.AddressCount > 1
                ? IoMappingDisplay.ContinuousWriteCategory
                : IoMappingDisplay.SingleWriteCategory;
        }

        return mapping.AddressCount > 1
            ? IoMappingDisplay.ContinuousReadCategory
            : IoMappingDisplay.SingleReadCategory;
    }

    private static string ResolveBusinessGroup(IoMappingEntity mapping, string category)
        => IoMappingDisplay.ResolveBusinessGroup(mapping.BusinessGroup, category);

    private static IDictionary<string, IoDataSectionModel> ResolveSectionBucket(
        string category,
        string direction,
        int addressCount,
        IDictionary<string, IoDataSectionModel> singleReadSections,
        IDictionary<string, IoDataSectionModel> continuousReadSections,
        IDictionary<string, IoDataSectionModel> singleWriteSections,
        IDictionary<string, IoDataSectionModel> continuousWriteSections)
    {
        if (string.Equals(category, IoMappingDisplay.SingleReadCategory, StringComparison.OrdinalIgnoreCase))
        {
            return singleReadSections;
        }

        if (string.Equals(category, IoMappingDisplay.ContinuousReadCategory, StringComparison.OrdinalIgnoreCase))
        {
            return continuousReadSections;
        }

        if (string.Equals(category, IoMappingDisplay.SingleWriteCategory, StringComparison.OrdinalIgnoreCase))
        {
            return singleWriteSections;
        }

        if (string.Equals(category, IoMappingDisplay.ContinuousWriteCategory, StringComparison.OrdinalIgnoreCase))
        {
            return continuousWriteSections;
        }

        if (string.Equals(direction, IoMappingOptionCatalog.DirectionWrite, StringComparison.OrdinalIgnoreCase))
        {
            return addressCount > 1 ? continuousWriteSections : singleWriteSections;
        }

        return addressCount > 1 ? continuousReadSections : singleReadSections;
    }
}

public sealed record IoViewMappingBuildResult(
    IReadOnlyList<IoInteractionRowModel> InteractionRows,
    IReadOnlyList<IoDataSectionModel> SingleReadSections,
    IReadOnlyList<IoDataSectionModel> ContinuousReadSections,
    IReadOnlyList<IoDataSectionModel> SingleWriteSections,
    IReadOnlyList<IoDataSectionModel> ContinuousWriteSections);
