using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Shared.Avalonia.Windowing;

namespace IIoT.Edge.Launcher;

public partial class ReleaseNotesWindow : Window
{
    private const int WindowCornerRadius = 12;

    public ReleaseNotesWindow()
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
    }

    public ReleaseNotesWindow(LauncherReleaseNotesDetailViewModel detail)
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        DataContext = detail ?? throw new ArgumentNullException(nameof(detail));
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CloseWindowButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
