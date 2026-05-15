using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

/// <summary>
/// 连续数组型 IO 的页面展示分组，只承载矩阵行列和展开状态。
/// </summary>
public sealed class IoContinuousReadMatrixSectionModel : ObservableObject
{
    private readonly string _expandedText;
    private readonly string _collapsedText;
    private bool _isExpanded;

    public IoContinuousReadMatrixSectionModel(string expandedText, string collapsedText)
    {
        _expandedText = expandedText;
        _collapsedText = collapsedText;
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public string Category { get; init; } = string.Empty;

    public string BusinessGroup { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public bool CanManualRead { get; init; } = true;

    public string Title { get; init; } = string.Empty;

    public string EmptySummary { get; init; } = "暂无连续值";

    public string SummaryFormat { get; init; } = "{0} 行 x {1} 项";

    public ObservableCollection<IoSignalModel> Columns { get; } = [];

    public ObservableCollection<IoContinuousReadMatrixRowModel> Rows { get; } = [];

    public IRelayCommand ToggleExpandedCommand { get; }

    public string Summary
    {
        get
        {
            var prefix = Rows.Count == 0 || Columns.Count == 0
                ? EmptySummary
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, SummaryFormat, Rows.Count, Columns.Count);

            var preview = Rows
                .Take(2)
                .SelectMany(static row => row.Values.Take(4).Select(value => $"{value.ColumnName}:{value.Value}"))
                .ToArray();

            return preview.Length == 0 ? prefix : $"{prefix}, {string.Join(", ", preview)}";
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ToggleText));
            }
        }
    }

    public string ToggleText => IsExpanded ? _expandedText : _collapsedText;

    public void RebuildRows()
    {
        Rows.Clear();

        var rowCount = Columns.Count == 0 ? 0 : Columns.Max(static column => column.ExpandedValues.Count);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = new IoContinuousReadMatrixRowModel(rowIndex + 1);
            foreach (var column in Columns)
            {
                row.Values.Add(new IoContinuousReadMatrixCellModel(
                    column.MatrixColumnTitle,
                    rowIndex < column.ExpandedValues.Count ? column.ExpandedValues[rowIndex].Value : "-"));
            }

            Rows.Add(row);
        }

        OnPropertyChanged(nameof(Summary));
    }
}

public sealed class IoContinuousReadMatrixRowModel(int index)
{
    public int Index { get; } = index;

    public ObservableCollection<IoContinuousReadMatrixCellModel> Values { get; } = [];
}

public sealed record IoContinuousReadMatrixCellModel(string ColumnName, string Value);
