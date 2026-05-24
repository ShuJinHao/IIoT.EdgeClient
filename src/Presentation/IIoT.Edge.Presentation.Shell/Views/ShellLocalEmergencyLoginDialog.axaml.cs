using Avalonia.Controls;
using Avalonia.Interactivity;
using IIoT.Edge.Presentation.Shell.Services;

namespace IIoT.Edge.Presentation.Shell.Views;

public partial class ShellLocalEmergencyLoginDialog : Window
{
    private IShellAuthContext? _authContext;

    public ShellLocalEmergencyLoginDialog()
    {
        InitializeComponent();
        Opened += (_, _) => PasswordInput.Focus();
    }

    public ShellLocalEmergencyLoginDialog(IShellAuthContext authContext)
        : this()
    {
        _authContext = authContext ?? throw new ArgumentNullException(nameof(authContext));
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
            var result = await _authContext.LoginLocalEmergencyAsync(PasswordInput.Text);
            if (result.Success)
            {
                Close(true);
                return;
            }

            ErrorText.Text = string.IsNullOrWhiteSpace(result.Message)
                ? "本地紧急登录失败。"
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
