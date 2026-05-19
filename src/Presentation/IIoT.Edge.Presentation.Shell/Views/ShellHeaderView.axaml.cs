using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using IIoT.Edge.UI.Shared.Avalonia.Controls;

namespace IIoT.Edge.Presentation.Shell.Views;

public partial class ShellHeaderView : UserControl
{
    public ShellHeaderView()
    {
        InitializeComponent();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || IsHeaderControl(e.Source as AvaloniaObject))
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }

    private static bool IsHeaderControl(AvaloniaObject? source)
    {
        if (source is not Visual visualSource)
        {
            return false;
        }

        foreach (var visual in visualSource.GetSelfAndVisualAncestors())
        {
            if (visual is Button or EdgeStatusChip)
            {
                return true;
            }
        }

        return false;
    }
}
