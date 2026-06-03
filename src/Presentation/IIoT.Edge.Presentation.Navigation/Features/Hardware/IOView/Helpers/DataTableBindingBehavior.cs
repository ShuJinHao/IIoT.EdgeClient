using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using IIoT.Edge.UI.Shared.Avalonia.Controls;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

/// <summary>
/// 附加行为：把连续读矩阵分组绑定到 DataGrid，并只在矩阵结构变化时生成列。
/// </summary>
public static class DataTableBindingBehavior
{
    /// <summary>
    /// 绑定源矩阵分组。运行时值刷新只更新行内 cell，不重建列和 ItemsSource。
    /// </summary>
    public static readonly AttachedProperty<IoContinuousReadMatrixSectionModel?> SourceSectionProperty =
        AvaloniaProperty.RegisterAttached<EdgeDataGrid, IoContinuousReadMatrixSectionModel?>(
            "SourceSection", typeof(DataTableBindingBehavior));

    static DataTableBindingBehavior()
    {
        SourceSectionProperty.Changed.AddClassHandler<EdgeDataGrid>(OnSourceSectionChanged);
    }

    public static IoContinuousReadMatrixSectionModel? GetSourceSection(AvaloniaObject obj)
        => obj.GetValue(SourceSectionProperty);

    public static void SetSourceSection(AvaloniaObject obj, IoContinuousReadMatrixSectionModel? value)
        => obj.SetValue(SourceSectionProperty, value);

    private static void OnSourceSectionChanged(EdgeDataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        grid.Columns.Clear();

        if (e.NewValue is not IoContinuousReadMatrixSectionModel section || section.Columns.Count == 0)
        {
            grid.ItemsSource = null;
            return;
        }

        grid.Columns.Add(new EdgeTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(IoContinuousReadMatrixRowModel.Index)),
            IsReadOnly = true,
            MinWidth = 64,
            Width = new DataGridLength(64)
        });

        for (var index = 0; index < section.Columns.Count; index++)
        {
            var column = section.Columns[index];
            grid.Columns.Add(new EdgeTextColumn
            {
                Header = column.MatrixColumnTitle,
                Binding = new Binding($"{nameof(IoContinuousReadMatrixRowModel.Values)}[{index}].{nameof(IoContinuousReadMatrixCellModel.Value)}"),
                IsReadOnly = true,
                MinWidth = 130,
                Width = new DataGridLength(130)
            });
        }

        grid.ItemsSource = section.Rows;
    }
}
