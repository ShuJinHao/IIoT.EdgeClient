using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
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

    public void RefreshRows()
    {
        var rowCount = Columns.Count == 0
            ? 0
            : Columns.Max(static column => column.ExpandedValues.Count);

        while (Rows.Count < rowCount)
        {
            Rows.Add(new IoContinuousReadMatrixRowModel
            {
                Index = Rows.Count + 1
            });
        }

        while (Rows.Count > rowCount)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = Rows[rowIndex];
            row.Index = rowIndex + 1;

            while (row.Values.Count < Columns.Count)
            {
                row.Values.Add(new IoContinuousReadMatrixCellModel());
            }

            while (row.Values.Count > Columns.Count)
            {
                row.Values.RemoveAt(row.Values.Count - 1);
            }

            for (var columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
            {
                var column = Columns[columnIndex];
                var cell = row.Values[columnIndex];
                cell.ColumnName = column.MatrixColumnTitle;
                cell.Value = rowIndex < column.ExpandedValues.Count
                    ? column.ExpandedValues[rowIndex].Value
                    : "-";
            }
        }

        OnPropertyChanged(nameof(Summary));
    }

    private string GetText(string key, string fallback)
        => _textProvider?.Invoke(key, fallback) ?? fallback;
}

public sealed class IoContinuousReadMatrixRowModel : BaseNotifyPropertyChanged
{
    private int _index;

    public int Index
    {
        get => _index;
        set
        {
            if (_index == value)
            {
                return;
            }

            _index = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<IoContinuousReadMatrixCellModel> Values { get; } = [];
}

public sealed class IoContinuousReadMatrixCellModel : BaseNotifyPropertyChanged
{
    private string _columnName = string.Empty;
    private string _value = string.Empty;

    public string ColumnName
    {
        get => _columnName;
        set
        {
            if (_columnName == value)
            {
                return;
            }

            _columnName = value;
            OnPropertyChanged();
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
        }
    }
}
