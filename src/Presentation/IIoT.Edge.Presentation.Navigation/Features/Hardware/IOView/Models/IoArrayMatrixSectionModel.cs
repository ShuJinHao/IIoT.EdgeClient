using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

/// <summary>
/// 连续数组型 IO 的页面展示分组，只承载矩阵行列和展开状态，不参与业务计算。
/// </summary>
public sealed class IoArrayMatrixSectionModel : BaseNotifyPropertyChanged
{
    private const string SinglePointDataCategory = "单点读数据";
    private const string ContinuousDataCategory = "连续读数据";

    private static readonly HashSet<string> GenericCategories =
    [
        SinglePointDataCategory,
        ContinuousDataCategory
    ];

    private bool _isExpanded;

    public IoArrayMatrixSectionModel()
    {
        ToggleExpandedCommand = new BaseCommand(_ => IsExpanded = !IsExpanded);
    }

    public string Category { get; init; } = ContinuousDataCategory;

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
                return LocalizeCategory(Category);
            }

            return GenericCategories.Contains(Category)
                ? GroupName
                : $"{LocalizeCategory(Category)} - {GroupName}";
        }
    }

    public string Summary
    {
        get
        {
            var rows = Rows.Count;
            var columns = Columns.Count;
            var prefix = rows == 0 || columns == 0
                ? GetText("Navigation_Io_NoContinuousValues", "暂无连续值")
                : string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    GetText("Navigation_Io_ArraySummaryFormat", "{0} 行 x {1} 列"),
                    rows,
                    columns);

            var preview = Rows
                .Take(2)
                .SelectMany(static row => row.Values.Take(4).Select(value => $"{value.ColumnName}:{value.Value}"))
                .ToArray();

            return preview.Length == 0
                ? prefix
                : $"{prefix}, {string.Join(", ", preview)}";
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

    public string ToggleText => IsExpanded
        ? GetText("Navigation_Io_CollapseDetails", "收起明细")
        : GetText("Navigation_Io_ViewDetails", "查看明细");

    public void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ToggleText));
    }

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

    private static string LocalizeCategory(string category)
        => category switch
        {
            SinglePointDataCategory => GetText("Navigation_Io_Category_SingleRead", category),
            ContinuousDataCategory => GetText("Navigation_Io_Category_ContinuousRead", category),
            _ => category
        };

    private static string GetText(string key, string fallback)
        => System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
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
