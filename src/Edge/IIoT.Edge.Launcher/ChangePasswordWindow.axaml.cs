using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IIoT.Edge.Launcher.ViewModels;

namespace IIoT.Edge.Launcher;

public partial class ChangePasswordWindow : Window
{
    private readonly LauncherMainViewModel _viewModel;

    public ChangePasswordWindow(LauncherMainViewModel viewModel, string? initialUserName)
    {
        InitializeComponent();
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
            DialogErrorText.Text = "账号不能为空。";
            UserNameInput.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(OldPasswordInput.Text))
        {
            DialogErrorText.Text = "旧密码不能为空。";
            OldPasswordInput.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(NewPasswordInput.Text))
        {
            DialogErrorText.Text = "新密码不能为空。";
            NewPasswordInput.Focus();
            return false;
        }

        if (NewPasswordInput.Text.Length < 6)
        {
            DialogErrorText.Text = "新密码至少 6 位。";
            NewPasswordInput.Focus();
            return false;
        }

        if (!string.Equals(NewPasswordInput.Text, ConfirmPasswordInput.Text, StringComparison.Ordinal))
        {
            DialogErrorText.Text = "两次输入的新密码不一致。";
            ConfirmPasswordInput.Focus();
            return false;
        }

        return true;
    }

    private void ClearPasswordBoxes()
    {
        OldPasswordInput.Text = string.Empty;
        NewPasswordInput.Text = string.Empty;
        ConfirmPasswordInput.Text = string.Empty;
    }
}
