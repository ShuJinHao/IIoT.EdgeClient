using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

internal sealed class IoViewMappingProjectionBuilder(Func<string, string, string> text)
{
    public IoViewMappingProjection Build(
        IReadOnlyCollection<IoMappingVm> mappings,
        Func<IoInteractionRowModel, Task> writeAsync,
        Func<IoInteractionRowModel, bool> canWrite)
    {
        var interactionRows = new List<IoInteractionRowModel>();
        var dataSections = new List<IoDataSectionModel>();
        var arraySections = new List<IoContinuousReadMatrixSectionModel>();

        var interactionGroups = mappings
            .Where(IsInteractionMapping)
            .GroupBy(static mapping => NormalizeGroup(mapping.BusinessGroup), StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Min(mapping => mapping.SortOrder));

        foreach (var group in interactionGroups)
        {
            var row = new IoInteractionRowModel
            {
                BusinessGroup = group.Key,
                SortOrder = group.Min(static mapping => mapping.SortOrder),
                ListSeparator = text("Navigation_ListSeparator", "、")
            };

            foreach (var mapping in group.OrderBy(static mapping => mapping.SortOrder))
            {
                var signal = CreateSignal(mapping);
                if (IsWrite(mapping.Direction))
                {
                    row.AddHostSignal(signal);
                }
                else
                {
                    row.AddPlcSignal(signal);
                }
            }

            row.WriteCommand = new AsyncRelayCommand(() => writeAsync(row), () => canWrite(row));
            row.InitializeWriteValueFromCurrentBuffer();
            interactionRows.Add(row);
        }

        var readMappings = mappings
            .Where(static mapping => !IsWrite(mapping.Direction))
            .Where(static mapping => !IsInteractionMapping(mapping))
            .OrderBy(static mapping => mapping.SortOrder)
            .ToArray();

        foreach (var group in readMappings.Where(static mapping => mapping.AddressCount <= 1)
            .GroupBy(static mapping => BuildSectionTitle(mapping), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var section = new IoDataSectionModel
            {
                Category = first.Category,
                BusinessGroup = NormalizeGroup(first.BusinessGroup),
                SortOrder = first.SortOrder,
                Title = group.Key,
                CanManualRead = true
            };
            foreach (var mapping in group)
            {
                section.Signals.Add(CreateSignal(mapping));
            }

            dataSections.Add(section);
        }

        foreach (var group in readMappings.Where(static mapping => mapping.AddressCount > 1)
            .GroupBy(static mapping => BuildSectionTitle(mapping), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var section = new IoContinuousReadMatrixSectionModel(
                text("Navigation_Io_CollapseDetails", "收起明细"),
                text("Navigation_Io_ViewDetails", "查看明细"))
            {
                Category = first.Category,
                BusinessGroup = NormalizeGroup(first.BusinessGroup),
                SortOrder = first.SortOrder,
                Title = group.Key,
                EmptySummary = text("Navigation_Io_NoContinuousValues", "暂无连续值"),
                SummaryFormat = text("Navigation_Io_ArraySummaryFormat", "{0} 行 x {1} 项")
            };

            foreach (var mapping in group)
            {
                section.Columns.Add(CreateSignal(mapping));
            }

            section.RebuildRows();
            arraySections.Add(section);
        }

        return new IoViewMappingProjection(interactionRows, dataSections, arraySections);
    }

    private IoSignalModel CreateSignal(IoMappingVm mapping)
        => new()
        {
            SignalKey = mapping.SignalKey,
            SignalName = string.IsNullOrWhiteSpace(mapping.SignalName) ? mapping.SignalKey : mapping.SignalName,
            PlcAddress = mapping.PlcAddress,
            AddressCount = Math.Max(1, mapping.AddressCount),
            Direction = mapping.Direction,
            DataType = mapping.DataType,
            DirectionText = IsWrite(mapping.Direction)
                ? text("Navigation_Io_Direction_HostToPlc", "上位机到 PLC")
                : text("Navigation_Io_Direction_PlcToHost", "PLC 到上位机"),
            Remark = mapping.Remark,
            SortOrder = mapping.SortOrder
        };

    private static string BuildSectionTitle(IoMappingVm mapping)
    {
        var category = string.IsNullOrWhiteSpace(mapping.Category) ? "I/O" : mapping.Category.Trim();
        var group = NormalizeGroup(mapping.BusinessGroup);
        return string.IsNullOrWhiteSpace(group) ? category : $"{category} / {group}";
    }

    private static string NormalizeGroup(string? value)
        => string.IsNullOrWhiteSpace(value) ? "未分组" : value.Trim();

    private static bool IsInteractionMapping(IoMappingVm mapping)
        => Contains(mapping.Category, "Interaction")
            || Contains(mapping.Category, "交互")
            || Contains(mapping.BusinessGroup, "Interaction")
            || Contains(mapping.BusinessGroup, "交互");

    private static bool IsWrite(string? direction)
        => string.Equals(direction, "Write", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string token)
        => value?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
}

internal sealed record IoViewMappingProjection(
    IReadOnlyList<IoInteractionRowModel> InteractionRows,
    IReadOnlyList<IoDataSectionModel> DataSections,
    IReadOnlyList<IoContinuousReadMatrixSectionModel> ArraySections);
