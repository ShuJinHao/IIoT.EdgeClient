using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.UI.Shared.PluginSystem;

/// <summary>
/// 视图模型基础类。
/// 为所有页面视图模型提供布局属性和激活生命周期默认实现。
/// </summary>
public abstract class ViewModelBase : BaseNotifyPropertyChanged, IViewModelContract
{
    public abstract string ViewId { get; }
    public abstract string ViewTitle { get; }

    private int _row;
    public int LayoutRow { get => _row; set { if (_row == value) return; _row = value; OnPropertyChanged(); } }

    private int _col;
    public int LayoutColumn { get => _col; set { if (_col == value) return; _col = value; OnPropertyChanged(); } }

    private int _rowSpan = 1;
    public int RowSpan { get => _rowSpan; set { if (_rowSpan == value) return; _rowSpan = value; OnPropertyChanged(); } }

    private int _colSpan = 1;
    public int ColumnSpan { get => _colSpan; set { if (_colSpan == value) return; _colSpan = value; OnPropertyChanged(); } }

    private bool _isVisible = true;
    public bool IsVisible { get => _isVisible; set { if (_isVisible == value) return; _isVisible = value; OnPropertyChanged(); } }

    public virtual Task OnActivatedAsync() => Task.CompletedTask;

    public virtual Task OnDeactivatedAsync() => Task.CompletedTask;
}
