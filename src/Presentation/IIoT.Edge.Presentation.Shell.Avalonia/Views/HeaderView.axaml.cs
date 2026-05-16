using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;

namespace IIoT.Edge.Presentation.Shell.Avalonia.Views;

public partial class HeaderView : UserControl
{
    public HeaderView()
    {
        InitializeComponent();
    }

    private void HeaderRoot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInteractiveSource(e.Source as Control))
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed &&
            TopLevel.GetTopLevel(this) is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void HeaderRoot_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsInteractiveSource(e.Source as Control) ||
            DataContext is not HeaderViewModel viewModel ||
            !viewModel.ToggleMaximizeCommand.CanExecute(null))
        {
            return;
        }

        viewModel.ToggleMaximizeCommand.Execute(null);
    }

    private static bool IsInteractiveSource(Control? source)
    {
        for (var current = source; current is not null; current = current.GetVisualParent() as Control)
        {
            if (current is HeaderView)
            {
                return false;
            }

            if (current is Button or TextBox or ComboBox)
            {
                return true;
            }
        }

        return false;
    }
}
