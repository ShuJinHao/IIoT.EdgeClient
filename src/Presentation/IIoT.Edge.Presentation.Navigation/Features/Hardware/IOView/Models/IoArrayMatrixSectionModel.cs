using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public sealed class IoArrayMatrixSectionModel : BaseNotifyPropertyChanged
{
    private static readonly HashSet<string> GenericCategories =
    [
        "单点读数据",
        "连续读数据"
    ];

    private bool _isExpanded;

    public IoArrayMatrixSectionModel()
    {
        ToggleExpandedCommand = new BaseCommand(_ => IsExpanded = !IsExpanded);
    }

    public string Category { get; init; } = "连续读数据";

    public string GroupName { get; init; } = string.Empty;

    public int SortOrder { get; set; }

    public ObservableCollection<IoSignalModel> Columns { get; } = [];

    public ObservableCollection<IoArrayMatrixRowModel> Rows { get; } = [];

    public ICommand ToggleExpandedCommand { get; }

    public string Title
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GroupName)
                || string.Equals(Category, GroupName, StringComparison.OrdinalIgnoreCase))
            {
                return Category;
            }

            return GenericCategories.Contains(Category)
                ? GroupName
                : $"{Category} - {GroupName}";
        }
    }

    public string Summary
    {
        get
        {
            var rows = Rows.Count;
            var columns = Columns.Count;
            var prefix = rows == 0 || columns == 0
                ? "暂无连续值"
                : $"{rows} 行 x {columns} 项";

            var preview = Rows
                .Take(2)
                .SelectMany(static row => row.Values.Take(4).Select(value => $"{value.ColumnName}:{value.Value}"))
                .ToArray();

            return preview.Length == 0
                ? prefix
                : $"{prefix}，{string.Join("，", preview)}";
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToggleText));
        }
    }

    public string ToggleText => IsExpanded ? "收起明细" : "查看明细";

    public void RebuildRows()
    {
        Rows.Clear();

        var rowCount = Columns.Count == 0
            ? 0
            : Columns.Max(static column => column.ExpandedValues.Count);

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = new IoArrayMatrixRowModel
            {
                Index = rowIndex + 1
            };

            foreach (var column in Columns)
            {
                row.Values.Add(new IoArrayMatrixCellModel
                {
                    ColumnName = column.MatrixColumnTitle,
                    Value = rowIndex < column.ExpandedValues.Count
                        ? column.ExpandedValues[rowIndex].Value
                        : "-"
                });
            }

            Rows.Add(row);
        }

        OnPropertyChanged(nameof(Summary));
    }
}

public sealed class IoArrayMatrixRowModel
{
    public int Index { get; init; }

    public ObservableCollection<IoArrayMatrixCellModel> Values { get; } = [];
}

public sealed class IoArrayMatrixCellModel
{
    public string ColumnName { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}
