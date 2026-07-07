using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Shared.Avalonia.Windowing;

namespace IIoT.Edge.Launcher;

public partial class ChangePasswordWindow : Window
{
    private const int WindowCornerRadius = 8;
    private readonly LauncherMainViewModel _viewModel;

    public ChangePasswordWindow()
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        _viewModel = null!;
    }

    public ChangePasswordWindow(LauncherMainViewModel viewModel, string? initialUserName)
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        UserNameInput.Text = initialUserName?.Trim() ?? string.Empty;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserNameInput.Text))
        {
            UserNameInput.Focus();
            return;
        }

        OldPasswordInput.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ClearPasswordBoxes();
    }

    private async void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        DialogErrorText.Text = string.Empty;
        if (!ValidateInput())
        {
            return;
        }

        var changed = await _viewModel.ChangePasswordAsync(
            UserNameInput.Text,
            OldPasswordInput.Text,
            NewPasswordInput.Text);

        if (!changed)
        {
            DialogErrorText.Text = _viewModel.ErrorMessage;
            return;
        }

        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
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
        Close(false);
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(UserNameInput.Text))
        {
            DialogErrorText.Text = Text("Launcher_ChangePassword_UserNameRequired");
            UserNameInput.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(OldPasswordInput.Text))
        {
            DialogErrorText.Text = Text("Launcher_ChangePassword_OldPasswordRequired");
            OldPasswordInput.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(NewPasswordInput.Text))
        {
            DialogErrorText.Text = Text("Launcher_ChangePassword_NewPasswordRequired");
            NewPasswordInput.Focus();
            return false;
        }

        if (LauncherPasswordPolicy.Validate(NewPasswordInput.Text) is not null)
        {
            DialogErrorText.Text = Text("Launcher_ChangePassword_NewPasswordMinLength");
            NewPasswordInput.Focus();
            return false;
        }

        if (!string.Equals(NewPasswordInput.Text, ConfirmPasswordInput.Text, StringComparison.Ordinal))
        {
            DialogErrorText.Text = Text("Launcher_ChangePassword_ConfirmMismatch");
            ConfirmPasswordInput.Focus();
            return false;
        }

        return true;
    }

    private string Text(string key)
        => _viewModel.GetText(key);

    private void ClearPasswordBoxes()
    {
        OldPasswordInput.Text = string.Empty;
        NewPasswordInput.Text = string.Empty;
        ConfirmPasswordInput.Text = string.Empty;
    }
}
