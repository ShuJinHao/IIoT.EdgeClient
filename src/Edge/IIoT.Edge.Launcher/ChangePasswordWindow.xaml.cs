using IIoT.Edge.Launcher.ViewModels;
using System.Windows;

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
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
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

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogErrorText.Text = string.Empty;
        if (!ValidateInput())
        {
            return;
        }

        var changed = await _viewModel.ChangePasswordAsync(
            UserNameInput.Text,
            OldPasswordInput.Password,
            NewPasswordInput.Password);

        if (!changed)
        {
            DialogErrorText.Text = _viewModel.ErrorMessage;
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(UserNameInput.Text))
        {
            DialogErrorText.Text = "账号不能为空。";
            UserNameInput.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(OldPasswordInput.Password))
        {
            DialogErrorText.Text = "旧密码不能为空。";
            OldPasswordInput.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(NewPasswordInput.Password))
        {
            DialogErrorText.Text = "新密码不能为空。";
            NewPasswordInput.Focus();
            return false;
        }

        if (NewPasswordInput.Password.Length < 6)
        {
            DialogErrorText.Text = "新密码至少 6 位。";
            NewPasswordInput.Focus();
            return false;
        }

        if (!string.Equals(NewPasswordInput.Password, ConfirmPasswordInput.Password, StringComparison.Ordinal))
        {
            DialogErrorText.Text = "两次输入的新密码不一致。";
            ConfirmPasswordInput.Focus();
            return false;
        }

        return true;
    }

    private void ClearPasswordBoxes()
    {
        OldPasswordInput.Clear();
        NewPasswordInput.Clear();
        ConfirmPasswordInput.Clear();
    }
}
