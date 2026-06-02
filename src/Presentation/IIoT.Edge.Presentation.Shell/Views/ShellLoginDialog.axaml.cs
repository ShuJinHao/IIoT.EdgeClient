using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IIoT.Edge.Presentation.Shell.Services;

namespace IIoT.Edge.Presentation.Shell.Views;

public partial class ShellLoginDialog : Window
{
    private const double MinimumOverlayWidth = 760;
    private const double MinimumOverlayHeight = 520;

    private IShellAuthContext? _authContext;
    private bool _isBusy;

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
                : await _authContext.LoginLocalEmergencyAsync(LocalPasswordInput.Text);

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
                LocalPasswordInput.Text = string.Empty;
                LocalPasswordInput.Focus();
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
        RefreshSubmitState();

        if (IsCloudMode)
        {
            EmployeeNoInput.Focus();
        }
        else
        {
            LocalPasswordInput.Focus();
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
}
