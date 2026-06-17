using System.Globalization;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Shared.Avalonia.Windowing;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Launcher;

public partial class MainWindow : Window
{
    private const int WindowCornerRadius = 16;
    private readonly LauncherMainViewModel _viewModel;
    private readonly IAppLanguageService _languageService;

    public MainWindow()
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        _viewModel = null!;
        _languageService = null!;
    }

    public MainWindow(
        LauncherMainViewModel viewModel,
        IAppLanguageService languageService)
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        DataContext = _viewModel;
        _viewModel.ClientReleasePanel.ConfirmVersionChangeAsync = ConfirmVersionChangeAsync;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Opened += OnOpened;
        Closed += OnClosed;
        UpdateVisualState();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        UserNameTextBox.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        PasswordInput.Text = string.Empty;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(LauncherMainViewModel.IsAuthenticated), StringComparison.Ordinal))
        {
            UpdateVisualState();
        }
    }

    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        await _viewModel.LoginAsync(UserNameTextBox.Text, PasswordInput.Text);
        if (_viewModel.IsAuthenticated)
        {
            PasswordInput.Text = string.Empty;
        }

        UpdateVisualState();
    }

    private async void ChangePasswordButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordWindow(_viewModel, UserNameTextBox.Text);
        var changed = await dialog.ShowDialog<bool>(this);
        if (changed)
        {
            PasswordInput.Text = string.Empty;
        }
    }

    private async void LaunchProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LauncherProfileCardViewModel card)
        {
            return;
        }

        await _viewModel.LaunchProfileCardAsync(card);
    }

    private async void RefreshUpdateCenterButton_Click(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshUpdateCenterAsync();
    }

    private async void InstallPluginUpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LauncherClientPluginItem row)
        {
            return;
        }

        await _viewModel.ExecuteUpdateRowActionAsync(row);
    }

    private void ToggleVersionComponentButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LauncherVersionComponentItem component)
        {
            return;
        }

        _viewModel.ClientReleasePanel.ToggleComponent(component);
    }

    private async void ApplyVersionButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LauncherVersionOptionItem option)
        {
            return;
        }

        await _viewModel.ClientReleasePanel.ApplyVersionAsync(option);
    }

    private async Task<bool> ConfirmVersionChangeAsync(LauncherVersionChangeConfirmationRequest request)
    {
        var dialog = new VersionChangeConfirmationWindow(request, _languageService);
        return await dialog.ShowDialog<bool>(this);
    }

    private void UpdateVisualState()
    {
        LoginPageRoot.IsVisible = !_viewModel.IsAuthenticated;
        SelectionPageRoot.IsVisible = _viewModel.IsAuthenticated;

        if (!_viewModel.IsAuthenticated && IsVisible)
        {
            UserNameTextBox.Focus();
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2 && CanResize)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        BeginMoveDrag(e);
    }

    private void MinimizeWindowButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleWindowStateButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void ToggleLanguageButton_Click(object? sender, RoutedEventArgs e)
    {
        var nextCultureName = string.Equals(_languageService.Current.Name, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : "zh-CN";

        _languageService.Change(CultureInfo.GetCultureInfo(nextCultureName));
    }

    private void CloseWindowButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
