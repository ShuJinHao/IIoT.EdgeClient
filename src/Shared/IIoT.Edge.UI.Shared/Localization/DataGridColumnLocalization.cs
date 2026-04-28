using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IIoT.Edge.UI.Shared.Localization;

/// <summary>
/// 为 DataGridColumn 表头提供基于 WPF 动态资源的显式刷新能力。
/// </summary>
public static class DataGridColumnLocalization
{
    private static readonly object SyncRoot = new();
    private static readonly List<WeakReference<DataGridColumn>> RegisteredColumns = [];

    public static readonly DependencyProperty HeaderResourceKeyProperty =
        DependencyProperty.RegisterAttached(
            "HeaderResourceKey",
            typeof(string),
            typeof(DataGridColumnLocalization),
            new PropertyMetadata(string.Empty, OnHeaderResourceKeyChanged));

    public static void SetHeaderResourceKey(DataGridColumn column, string value)
        => column.SetValue(HeaderResourceKeyProperty, value);

    public static string GetHeaderResourceKey(DataGridColumn column)
        => (string)column.GetValue(HeaderResourceKeyProperty);

    public static void RefreshOpenWindows()
    {
        RefreshRegisteredColumns();

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        foreach (Window window in app.Windows)
        {
            RefreshElement(window);
        }
    }

    public static void RefreshElement(DependencyObject root)
    {
        foreach (var dataGrid in EnumerateDescendants<DataGrid>(root))
        {
            RefreshColumns(dataGrid);
        }
    }

    public static void RefreshColumns(DataGrid dataGrid)
    {
        foreach (var column in dataGrid.Columns)
        {
            ApplyHeader(column);
        }
    }

    private static void OnHeaderResourceKeyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is DataGridColumn column)
        {
            RegisterColumn(column);
            ApplyHeader(column);
        }
    }

    private static void RegisterColumn(DataGridColumn column)
    {
        lock (SyncRoot)
        {
            foreach (var reference in RegisteredColumns)
            {
                if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, column))
                {
                    return;
                }
            }

            RegisteredColumns.Add(new WeakReference<DataGridColumn>(column));
        }
    }

    private static void RefreshRegisteredColumns()
    {
        List<DataGridColumn> liveColumns = [];

        lock (SyncRoot)
        {
            for (var index = RegisteredColumns.Count - 1; index >= 0; index--)
            {
                if (!RegisteredColumns[index].TryGetTarget(out var column))
                {
                    RegisteredColumns.RemoveAt(index);
                    continue;
                }

                liveColumns.Add(column);
            }
        }

        foreach (var column in liveColumns)
        {
            ApplyHeader(column);
        }
    }

    private static void ApplyHeader(DataGridColumn column)
    {
        var resourceKey = GetHeaderResourceKey(column);
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return;
        }

        column.Header = Application.Current?.TryFindResource(resourceKey) as string ?? resourceKey;
    }

    private static IEnumerable<T> EnumerateDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var visited = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current is T matched)
            {
                yield return matched;
            }

            foreach (var child in GetChildren(current))
            {
                stack.Push(child);
            }
        }
    }

    private static IEnumerable<DependencyObject> GetChildren(DependencyObject parent)
    {
        var visualCount = GetVisualChildrenCount(parent);
        for (var i = 0; i < visualCount; i++)
        {
            yield return VisualTreeHelper.GetChild(parent, i);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
        }
    }

    private static int GetVisualChildrenCount(DependencyObject parent)
    {
        try
        {
            return VisualTreeHelper.GetChildrenCount(parent);
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}
