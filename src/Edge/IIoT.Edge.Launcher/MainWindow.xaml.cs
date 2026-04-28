using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace IIoT.Edge.Launcher;

public partial class MainWindow : Window
{
    private readonly LauncherMainViewModel _viewModel;

    public MainWindow(LauncherMainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        UpdateVisualState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UserNameTextBox.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(LauncherMainViewModel.IsAuthenticated), StringComparison.Ordinal))
        {
            UpdateVisualState();
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoginAsync(UserNameTextBox.Text, PasswordInput.Password);
        if (_viewModel.IsAuthenticated)
        {
            PasswordInput.Clear();
        }

        UpdateVisualState();
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordWindow(_viewModel, UserNameTextBox.Text)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            PasswordInput.Clear();
        }
    }

    private async void LaunchProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LauncherProfileDefinition profile)
        {
            await _viewModel.LaunchAsync(profile);
        }
    }

    private void UpdateVisualState()
    {
        LoginPageRoot.Visibility = _viewModel.IsAuthenticated ? Visibility.Collapsed : Visibility.Visible;
        SelectionPageRoot.Visibility = _viewModel.IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;

        if (!_viewModel.IsAuthenticated)
        {
            UserNameTextBox.Focus();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleWindowStateButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
