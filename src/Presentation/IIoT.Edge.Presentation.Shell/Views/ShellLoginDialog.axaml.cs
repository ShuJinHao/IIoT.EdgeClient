using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Presentation.Shell.Services;

namespace IIoT.Edge.Presentation.Shell.Views;

public partial class ShellLoginDialog : Window
{
    private const double MinimumOverlayWidth = 760;
    private const double MinimumOverlayHeight = 520;

    private IShellAuthContext? _authContext;
    private bool _isBusy;
    private LocalEmergencyMode _localMode;

    public ShellLoginDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            Activate();
            Topmost = true;
            Topmost = false;
            RefreshMode();
            LocalPasswordInput.Focus();
        };
    }

    public ShellLoginDialog(IShellAuthContext authContext)
        : this()
    {
        _authContext = authContext ?? throw new ArgumentNullException(nameof(authContext));
    }

    public void PrepareForOwner(Window owner)
    {
        Width = Math.Max(MinimumOverlayWidth, owner.Bounds.Width);
        Height = Math.Max(MinimumOverlayHeight, owner.Bounds.Height);
        Position = owner.Position;
    }

    private bool IsCloudMode => LoginModeTabs.SelectedIndex == 1;

    private enum LocalEmergencyMode
    {
        Login,
        Initialize,
        Reset
    }

    private void OnModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshMode();
    }

    private async void OnLoginClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await SubmitAsync();
    }

    private async void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        if (_authContext is null || _isBusy || (IsCloudMode && !_authContext.HasCloudDeviceIdentity))
        {
            return;
        }

        _isBusy = true;
        LoginButton.IsEnabled = false;
        ErrorText.Text = string.Empty;

        try
        {
            var result = IsCloudMode
                ? await _authContext.LoginCloudEmployeeAsync(EmployeeNoInput.Text, CloudPasswordInput.Text)
                : await SubmitLocalAsync();

            if (result.Success)
            {
                Close(true);
                return;
            }

            ErrorText.Text = string.IsNullOrWhiteSpace(result.Message)
                ? ResourceText(IsCloudMode ? "Shell_Login_CloudFailed" : "Shell_Login_LocalFailed")
                : result.Message;

            if (IsCloudMode)
            {
                CloudPasswordInput.Text = string.Empty;
                CloudPasswordInput.Focus();
            }
            else
            {
                ClearLocalPasswordFields();
                FocusLocalInput();
            }
        }
        finally
        {
            _isBusy = false;
            RefreshSubmitState();
        }
    }

    private void RefreshMode()
    {
        if (LocalForm is null || CloudForm is null)
        {
            return;
        }

        ErrorText.Text = string.Empty;
        LocalForm.IsVisible = !IsCloudMode;
        CloudForm.IsVisible = IsCloudMode;
        CloudUnavailableNotice.IsVisible = IsCloudMode && _authContext?.HasCloudDeviceIdentity != true;
        RefreshLocalMode();
        RefreshSubmitState();

        if (IsCloudMode)
        {
            EmployeeNoInput.Focus();
        }
        else
        {
            FocusLocalInput();
        }
    }

    private void RefreshSubmitState()
    {
        LoginButton.IsEnabled = _authContext is not null
            && !_isBusy
            && (!IsCloudMode || _authContext.HasCloudDeviceIdentity);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close(false);
    }

    private string ResourceText(string key)
        => this.TryFindResource(key, out var value) && value is string text
            ? text
            : string.Empty;

    private Task<AuthResult> SubmitLocalAsync()
    {
        if (_authContext is null)
        {
            return Task.FromResult(AuthResult.Fail(ResourceText("Shell_Login_LocalFailed")));
        }

        return _localMode switch
        {
            LocalEmergencyMode.Initialize => SubmitLocalInitializeAsync(),
            LocalEmergencyMode.Reset => SubmitLocalResetAsync(),
            _ => _authContext.LoginLocalEmergencyAsync(LocalPasswordInput.Text)
        };
    }

    private Task<AuthResult> SubmitLocalInitializeAsync()
    {
        if (_authContext is null)
        {
            return Task.FromResult(AuthResult.Fail(ResourceText("Shell_Login_LocalFailed")));
        }

        var validation = ValidateNewPasswordPair(
            LocalSetupNewPasswordInput.Text,
            LocalSetupConfirmPasswordInput.Text);
        return validation is not null
            ? Task.FromResult(AuthResult.Fail(validation))
            : _authContext.InitializeLocalEmergencyAdminAsync(LocalSetupNewPasswordInput.Text);
    }

    private Task<AuthResult> SubmitLocalResetAsync()
    {
        if (_authContext is null)
        {
            return Task.FromResult(AuthResult.Fail(ResourceText("Shell_Login_LocalFailed")));
        }

        if (string.IsNullOrWhiteSpace(LocalResetCurrentPasswordInput.Text))
        {
            return Task.FromResult(AuthResult.Fail(ResourceText("Shell_Login_CurrentPasswordRequired")));
        }

        var validation = ValidateNewPasswordPair(
            LocalResetNewPasswordInput.Text,
            LocalResetConfirmPasswordInput.Text);
        return validation is not null
            ? Task.FromResult(AuthResult.Fail(validation))
            : _authContext.ResetLocalEmergencyPasswordAsync(
                LocalResetCurrentPasswordInput.Text,
                LocalResetNewPasswordInput.Text);
    }

    private string? ValidateNewPasswordPair(string? newPassword, string? confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return ResourceText("Shell_Login_NewPasswordRequired");
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return ResourceText("Shell_Login_ConfirmPasswordMismatch");
        }

        return null;
    }

    private void RefreshLocalMode()
    {
        if (_authContext is null)
        {
            _localMode = LocalEmergencyMode.Login;
        }
        else
        {
            _localMode = _authContext.LocalAdminCredentialStatus switch
            {
                LocalAdminCredentialStatus.NotConfigured or LocalAdminCredentialStatus.Invalid
                    => LocalEmergencyMode.Initialize,
                LocalAdminCredentialStatus.RequiresPasswordReset => LocalEmergencyMode.Reset,
                _ => LocalEmergencyMode.Login
            };
        }

        LocalLoginPanel.IsVisible = _localMode == LocalEmergencyMode.Login;
        LocalSetupPanel.IsVisible = _localMode == LocalEmergencyMode.Initialize;
        LocalResetPanel.IsVisible = _localMode == LocalEmergencyMode.Reset;
        LocalDescriptionText.Text = _localMode switch
        {
            LocalEmergencyMode.Initialize => ResourceText("Shell_Login_LocalInitializeDescription"),
            LocalEmergencyMode.Reset => ResourceText("Shell_Login_LocalResetDescription"),
            _ => ResourceText("Shell_Login_LocalDescription")
        };
        LoginButton.Content = IsCloudMode
            ? ResourceText("Shell_Login_Submit")
            : _localMode switch
        {
            LocalEmergencyMode.Initialize => ResourceText("Shell_Login_InitializeSubmit"),
            LocalEmergencyMode.Reset => ResourceText("Shell_Login_ResetSubmit"),
            _ => ResourceText("Shell_Login_Submit")
        };
    }

    private void FocusLocalInput()
    {
        switch (_localMode)
        {
            case LocalEmergencyMode.Initialize:
                LocalSetupNewPasswordInput.Focus();
                break;
            case LocalEmergencyMode.Reset:
                LocalResetCurrentPasswordInput.Focus();
                break;
            default:
                LocalPasswordInput.Focus();
                break;
        }
    }

    private void ClearLocalPasswordFields()
    {
        LocalPasswordInput.Text = string.Empty;
        LocalSetupNewPasswordInput.Text = string.Empty;
        LocalSetupConfirmPasswordInput.Text = string.Empty;
        LocalResetCurrentPasswordInput.Text = string.Empty;
        LocalResetNewPasswordInput.Text = string.Empty;
        LocalResetConfirmPasswordInput.Text = string.Empty;
    }
}
