using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Shared.Avalonia.Windowing;

namespace IIoT.Edge.Launcher;

public partial class VersionHistoryWindow : Window
{
    private const int WindowCornerRadius = 12;
    private readonly LauncherClientReleasePanelViewModel _panel;

    public VersionHistoryWindow()
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        _panel = null!;
    }

    public VersionHistoryWindow(
        LauncherVersionComponentItem component,
        LauncherClientReleasePanelViewModel panel)
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        DataContext = new LauncherVersionHistoryViewModel(component, _panel);
    }

    private async void ApplyVersionButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LauncherVersionOptionItem option)
        {
            return;
        }

        await _panel.ApplyVersionAsync(option);
    }

    private async void OpenReleaseNotesButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LauncherVersionOptionItem option)
        {
            return;
        }

        var componentKindText = (DataContext as LauncherVersionHistoryViewModel)?.Component.ComponentKindText ?? "-";
        var dialog = new ReleaseNotesWindow(LauncherReleaseNotesDetailViewModel.FromVersionOption(option, componentKindText));
        await dialog.ShowDialog(this);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseWindowButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
