using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

internal sealed class IoViewMappingBuilder
{
    public IoViewMappingBuildResult Build(IEnumerable<IoMappingEntity> mappings)
    {
        var readIndex = 0;
        var writeIndex = 0;
        var interactionRows = new Dictionary<string, IoInteractionRowModel>(StringComparer.OrdinalIgnoreCase);
        var dataSections = new Dictionary<string, IoDataSectionModel>(StringComparer.OrdinalIgnoreCase);
        var arraySections = new Dictionary<string, IoContinuousReadMatrixSectionModel>(StringComparer.OrdinalIgnoreCase);

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

            if (IoMappingDisplay.IsContinuousMatrix(signal.DataType, signal.AddressCount))
            {
                var arraySection = GetOrCreateArraySection(arraySections, mapping, category);
                arraySection.SortOrder = Math.Min(arraySection.SortOrder, mapping.SortOrder);
                arraySection.Columns.Add(signal);
                continue;
            }

            var section = GetOrCreateDataSection(dataSections, mapping, category);
            section.SortOrder = Math.Min(section.SortOrder, mapping.SortOrder);
            section.Signals.Add(signal);
        }

        return new IoViewMappingBuildResult(
            SortRows(interactionRows.Values),
            SortDataSections(dataSections.Values),
            SortArraySections(arraySections.Values));
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

    private static IReadOnlyList<IoContinuousReadMatrixSectionModel> SortArraySections(
        IEnumerable<IoContinuousReadMatrixSectionModel> sections)
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

    private static IoContinuousReadMatrixSectionModel GetOrCreateArraySection(
        IDictionary<string, IoContinuousReadMatrixSectionModel> sections,
        IoMappingEntity mapping,
        string category)
    {
        var businessGroup = ResolveBusinessGroup(mapping, category);
        var key = category;
        if (sections.TryGetValue(key, out var section))
        {
            return section;
        }

        section = new IoContinuousReadMatrixSectionModel
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
            SignalName = mapping.SignalName,
            Remark = mapping.Remark,
            DataType = mapping.DataType,
            StartIndex = startIndex,
            AddressCount = Math.Max(1, mapping.AddressCount),
            SortOrder = mapping.SortOrder
        };
    }

    private static string ResolveCategory(IoMappingEntity mapping)
        => IoMappingDisplay.ResolveCategory(mapping.Category, mapping.AddressCount);

    private static string ResolveBusinessGroup(IoMappingEntity mapping, string category)
        => IoMappingDisplay.ResolveBusinessGroup(mapping.BusinessGroup, category);
}

internal sealed record IoViewMappingBuildResult(
    IReadOnlyList<IoInteractionRowModel> InteractionRows,
    IReadOnlyList<IoDataSectionModel> DataSections,
    IReadOnlyList<IoContinuousReadMatrixSectionModel> ArraySections);
