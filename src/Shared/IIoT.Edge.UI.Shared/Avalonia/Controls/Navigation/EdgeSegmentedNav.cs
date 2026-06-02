using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 二级导航分段控件，选择变化时执行调用方提供的真实命令。
/// </summary>
public class EdgeSegmentedNav : ListBox
{
    public static readonly StyledProperty<ICommand?> ItemCommandProperty =
        AvaloniaProperty.Register<EdgeSegmentedNav, ICommand?>(nameof(ItemCommand));

    public ICommand? ItemCommand
    {
        get => GetValue(ItemCommandProperty);
        set => SetValue(ItemCommandProperty, value);
    }

    public EdgeSegmentedNav()
    {
        SelectionChanged += (_, _) => ExecuteSelectedItemCommand();
    }

    private void ExecuteSelectedItemCommand()
    {
        if (SelectedItem is null || ItemCommand is null)
        {
            return;
        }

        if (ItemCommand.CanExecute(SelectedItem))
        {
            ItemCommand.Execute(SelectedItem);
        }
    }
}
