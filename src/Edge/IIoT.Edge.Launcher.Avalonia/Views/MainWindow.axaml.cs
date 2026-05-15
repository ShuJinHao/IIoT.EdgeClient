using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.ViewModels;

namespace IIoT.Edge.Launcher.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly LauncherMainViewModel _viewModel;
    private readonly Border _loginPanel;
    private readonly Border _profilePanel;
    private readonly TextBox _userNameInput;
    private readonly TextBox _passwordInput;

    public MainWindow()
        : this(LauncherDesignTimeViewModelFactory.Create())
    {
    }

    public MainWindow(LauncherMainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _loginPanel = this.FindControl<Border>("LoginPanel") ?? throw new InvalidOperationException(nameof(LoginPanel));
        _profilePanel = this.FindControl<Border>("ProfilePanel") ?? throw new InvalidOperationException(nameof(ProfilePanel));
        _userNameInput = this.FindControl<TextBox>("UserNameInput") ?? throw new InvalidOperationException(nameof(UserNameInput));
        _passwordInput = this.FindControl<TextBox>("PasswordInput") ?? throw new InvalidOperationException(nameof(PasswordInput));
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(LauncherMainViewModel.IsAuthenticated), StringComparison.Ordinal))
            {
                UpdateVisualState();
            }
        };
        UpdateVisualState();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void LoginButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _viewModel.LoginAsync(_userNameInput.Text, _passwordInput.Text);
        if (_viewModel.IsAuthenticated)
        {
            _passwordInput.Text = string.Empty;
        }

        UpdateVisualState();
    }

    private async void ChangePasswordButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new ChangePasswordWindow(_viewModel, _userNameInput.Text);
        var changed = await dialog.ShowDialog<bool?>(this);
        if (changed == true)
        {
            _passwordInput.Text = string.Empty;
        }
    }

    private async void LaunchProfileButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is LauncherProfileDefinition profile)
        {
            await _viewModel.LaunchAsync(profile);
        }
    }

    private void UpdateVisualState()
    {
        _loginPanel.IsVisible = !_viewModel.IsAuthenticated;
        _profilePanel.IsVisible = _viewModel.IsAuthenticated;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeWindowButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleWindowStateButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindowButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
