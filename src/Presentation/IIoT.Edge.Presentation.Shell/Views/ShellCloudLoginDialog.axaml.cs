using Avalonia.Controls;
using Avalonia.Interactivity;
using IIoT.Edge.Presentation.Shell.Services;

namespace IIoT.Edge.Presentation.Shell.Views;

public partial class ShellCloudLoginDialog : Window
{
    private IShellAuthContext? _authContext;

    public ShellCloudLoginDialog()
    {
        InitializeComponent();
        Opened += (_, _) => EmployeeNoInput.Focus();
    }

    public ShellCloudLoginDialog(IShellAuthContext authContext)
        : this()
    {
        _authContext = authContext ?? throw new ArgumentNullException(nameof(authContext));
        if (!_authContext.HasCloudDeviceIdentity)
        {
            ErrorText.Text = "设备尚未完成云端身份初始化，无法进行云端员工登录。";
            LoginButton.IsEnabled = false;
        }
    }

    private async void OnLoginClick(object? sender, RoutedEventArgs e)
    {
        if (_authContext is null)
        {
            return;
        }

        LoginButton.IsEnabled = false;
        ErrorText.Text = string.Empty;

        try
        {
            var result = await _authContext.LoginCloudEmployeeAsync(EmployeeNoInput.Text, PasswordInput.Text);
            if (result.Success)
            {
                Close(true);
                return;
            }

            ErrorText.Text = string.IsNullOrWhiteSpace(result.Message)
                ? "云端员工登录失败。"
                : result.Message;
            PasswordInput.Text = string.Empty;
            PasswordInput.Focus();
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
        => Close(false);
}
