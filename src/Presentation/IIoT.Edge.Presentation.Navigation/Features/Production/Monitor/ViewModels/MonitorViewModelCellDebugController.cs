using IIoT.Edge.Application.Features.Production.Monitor;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

internal static class MonitorViewModelCellDebugController
{
    public static IReadOnlyList<MonitorCellDebugItemViewModel> CreateFilteredItems(
        IReadOnlyList<MonitorCellDebugSnapshot> rows,
        string queryText)
        => FilterRows(rows, queryText)
            .Select(static row => new MonitorCellDebugItemViewModel(row))
            .ToList();

    public static MonitorCellDebugItemViewModel? ResolveSelectedCell(
        IEnumerable<MonitorCellDebugItemViewModel> items,
        string? selectedKey)
    {
        var itemList = items as IReadOnlyList<MonitorCellDebugItemViewModel> ?? items.ToList();
        var selectedCell = !string.IsNullOrWhiteSpace(selectedKey)
            ? itemList.FirstOrDefault(item => string.Equals(item.InternalKey, selectedKey, StringComparison.Ordinal))
            : null;

        return selectedCell ?? itemList.FirstOrDefault();
    }

    private static IEnumerable<MonitorCellDebugSnapshot> FilterRows(
        IReadOnlyList<MonitorCellDebugSnapshot> rows,
        string queryText)
    {
        var query = queryText.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return rows;
        }

        return rows.Where(row =>
            Contains(row.InternalKey, query)
            || Contains(row.DisplayLabel, query));
    }

    private static bool Contains(string value, string query)
        => value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
}
