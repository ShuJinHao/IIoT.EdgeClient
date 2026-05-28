using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

/// <summary>
/// 附加行为：将 DataTable 绑定到 DataGrid，自动生成对应列。
/// Avalonia 的 DataGrid 不支持 DataRowView 索引器绑定（WPF 特有能力），
/// 因此将每行转为 Dictionary&lt;string, object&gt; 再绑定，保证 [key] 路径能正确解析。
/// </summary>
public static class DataTableBindingBehavior
{
    /// <summary>
    /// 绑定源 DataTable。当值变化时自动重建 DataGrid 列并设置 ItemsSource。
    /// </summary>
    public static readonly AttachedProperty<DataTable?> SourceTableProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, DataTable?>(
            "SourceTable", typeof(DataTableBindingBehavior));

    static DataTableBindingBehavior()
    {
        SourceTableProperty.Changed.AddClassHandler<DataGrid>(OnSourceTableChanged);
    }

    public static DataTable? GetSourceTable(AvaloniaObject obj) => obj.GetValue(SourceTableProperty);

    public static void SetSourceTable(AvaloniaObject obj, DataTable? value) => obj.SetValue(SourceTableProperty, value);

    private static void OnSourceTableChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        grid.Columns.Clear();

        if (e.NewValue is not DataTable table || table.Columns.Count == 0)
        {
            grid.ItemsSource = null;
            return;
        }

        // 生成列定义
        foreach (DataColumn col in table.Columns)
        {
            // Caption 用于显示表头，ColumnName 用于 Dictionary key 绑定
            var header = string.IsNullOrEmpty(col.Caption) ? col.ColumnName : col.Caption;

            var width = col.Ordinal == 0
                ? new DataGridLength(64)
                : new DataGridLength(130);

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"[{col.ColumnName}]"),
                IsReadOnly = true,
                MinWidth = width.Value,
                Width = width,
            });
        }

        // 将 DataTable 行转为 Dictionary 列表，Avalonia 绑定引擎能正确解析字典索引器
        var rows = new List<Dictionary<string, object>>(table.Rows.Count);
        foreach (DataRow dataRow in table.Rows)
        {
            var dict = new Dictionary<string, object>(table.Columns.Count);
            foreach (DataColumn col in table.Columns)
            {
                dict[col.ColumnName] = dataRow[col]?.ToString() ?? string.Empty;
            }

            rows.Add(dict);
        }

        grid.ItemsSource = rows;
    }
}
