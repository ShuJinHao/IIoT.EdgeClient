using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using IIoT.Edge.Launcher.ViewModels;

namespace IIoT.Edge.Launcher.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly LauncherMainViewModel _viewModel;

    public MainWindow()
        : this(LauncherDesignTimeViewModelFactory.Create())
    {
    }

    public MainWindow(LauncherMainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        _viewModel.LoginViewModel.ChangePasswordRequested += HandleChangePasswordRequested;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void HandleChangePasswordRequested(object? sender, EventArgs e)
    {
        var dialog = new ChangePasswordWindow(_viewModel, _viewModel.LoginViewModel.UserName);
        var changed = await dialog.ShowDialog<bool?>(this);
        if (changed == true)
        {
            _viewModel.LoginViewModel.Password = string.Empty;
        }
    }

    private void DragSurface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsFromInteractiveElement(e.Source))
        {
            BeginMoveDrag(e);
        }
    }

    private void DragSurface_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!IsFromInteractiveElement(e.Source))
        {
            ToggleWindowState();
        }
    }

    private void MinimizeWindowButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleWindowStateButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindowButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private static bool IsFromInteractiveElement(object? source)
    {
        if (source is not Control control)
        {
            return false;
        }

        if (control is Button or TextBox or ScrollViewer or ItemsControl)
        {
            return true;
        }

        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
        {
            if (ancestor is Button or TextBox or ScrollViewer or ItemsControl)
            {
                return true;
            }
        }

        return false;
    }
}
