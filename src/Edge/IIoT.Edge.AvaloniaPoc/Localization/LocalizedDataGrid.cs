using Avalonia;
using Avalonia.Controls;
using IIoT.Edge.AvaloniaPoc.Services;

namespace IIoT.Edge.AvaloniaPoc.Localization;

public sealed class LocalizedDataGrid
{
    public static readonly AttachedProperty<string?> HeaderResourceKeyProperty =
        AvaloniaProperty.RegisterAttached<LocalizedDataGrid, DataGridColumn, string?>("HeaderResourceKey");

    private static readonly List<WeakReference<DataGridColumn>> Columns = new();

    static LocalizedDataGrid()
    {
        HeaderResourceKeyProperty.Changed.AddClassHandler<DataGridColumn>((column, _) =>
        {
            Track(column);
            RefreshHeader(column);
        });
    }

    public static string? GetHeaderResourceKey(DataGridColumn column)
    {
        return column.GetValue(HeaderResourceKeyProperty);
    }

    public static void SetHeaderResourceKey(DataGridColumn column, string? value)
    {
        column.SetValue(HeaderResourceKeyProperty, value);
    }

    public static void RefreshHeaders()
    {
        for (var index = Columns.Count - 1; index >= 0; index--)
        {
            if (Columns[index].TryGetTarget(out var column))
            {
                RefreshHeader(column);
            }
            else
            {
                Columns.RemoveAt(index);
            }
        }
    }

    private static void Track(DataGridColumn column)
    {
        if (Columns.Any(reference => reference.TryGetTarget(out var current) && ReferenceEquals(current, column)))
        {
            return;
        }

        Columns.Add(new WeakReference<DataGridColumn>(column));
    }

    private static void RefreshHeader(DataGridColumn column)
    {
        var key = GetHeaderResourceKey(column);
        if (!string.IsNullOrWhiteSpace(key))
        {
            column.Header = AvaloniaAppLanguageService.FindText(key);
        }
    }
}
