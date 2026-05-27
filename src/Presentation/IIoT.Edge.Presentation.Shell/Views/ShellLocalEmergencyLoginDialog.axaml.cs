using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using IIoT.Edge.Presentation.Shell.Services;

namespace IIoT.Edge.Presentation.Shell.Views;

public partial class ShellLocalEmergencyLoginDialog : Window
{
    private const int WindowCornerRadius = 28;

    private IShellAuthContext? _authContext;

    public ShellLocalEmergencyLoginDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            RefreshRoundedWindowRegion();
            PasswordInput.Focus();
        };
        SizeChanged += (_, _) => RefreshRoundedWindowRegion();
        Closed += (_, _) => ClearRoundedWindowRegion();
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

    #region Windows 圆角窗口

    private void RefreshRoundedWindowRegion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var scale = RenderScaling;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale));
        var radius = Math.Max(1, (int)Math.Round(WindowCornerRadius * scale));
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == nint.Zero)
        {
            return;
        }

        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void ClearRoundedWindowRegion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle != nint.Zero)
        {
            SetWindowRgn(handle, nint.Zero, true);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(
        int left, int top, int right, int bottom,
        int widthEllipse, int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint windowHandle, nint regionHandle, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint objectHandle);

    #endregion
}
