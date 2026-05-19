using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

/// <summary>
/// 连续数组型 IO 的页面展示分组，只承载矩阵行列和展开状态，不参与业务计算。
/// </summary>
public sealed class IoContinuousReadMatrixSectionModel : BaseNotifyPropertyChanged
{
    private Func<string, string, string>? _textProvider;
    private bool _isExpanded;

    public IoContinuousReadMatrixSectionModel()
    {
        ToggleExpandedCommand = new BaseCommand(_ => IsExpanded = !IsExpanded);
    }

    public string Category { get; init; } = IoMappingDisplay.ContinuousReadCategory;

    public string BusinessGroup { get; init; } = string.Empty;

    public int SortOrder { get; set; }

    public bool CanManualRead => IoMappingOptionCatalog.IsReadDataCategory(Category);

    public ObservableCollection<IoSignalModel> Columns { get; } = [];

    public ObservableCollection<IoContinuousReadMatrixRowModel> Rows { get; } = [];

    public ICommand ToggleExpandedCommand { get; }

    public string Title => IoMappingDisplay.BuildSectionTitle(Category, BusinessGroup);

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
                    GetText("Navigation_Io_ArraySummaryFormat", "{0} 行 x {1} 项"),
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

    public void SetTextProvider(Func<string, string, string> textProvider)
    {
        _textProvider = textProvider;
        foreach (var column in Columns)
        {
            column.SetTextProvider(textProvider);
        }

        NotifyLocalizationChanged();
    }

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
            var row = new IoContinuousReadMatrixRowModel
            {
                Index = rowIndex + 1
            };

            foreach (var column in Columns)
            {
                row.Values.Add(new IoContinuousReadMatrixCellModel
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

    private string GetText(string key, string fallback)
        => _textProvider?.Invoke(key, fallback) ?? fallback;
}

public sealed class IoContinuousReadMatrixRowModel
{
    public int Index { get; init; }

    public ObservableCollection<IoContinuousReadMatrixCellModel> Values { get; } = [];
}

public sealed class IoContinuousReadMatrixCellModel
{
    public string ColumnName { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}
