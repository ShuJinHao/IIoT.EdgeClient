using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace IIoT.Edge.Shell;

public partial class ShellCrashDialog : Window
{
    public ShellCrashDialog()
        : this("应用启动失败。")
    {
    }

    public ShellCrashDialog(string message)
    {
        InitializeComponent();
        DataContext = new ShellCrashDialogViewModel(message);
    }

    private void OnDialogPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || IsCloseButton(e.Source as AvaloniaObject))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static bool IsCloseButton(AvaloniaObject? source)
    {
        if (source is not Visual visualSource)
        {
            return false;
        }

        foreach (var visual in visualSource.GetSelfAndVisualAncestors())
        {
            if (visual is Button)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record ShellCrashDialogViewModel(string Message);
}
