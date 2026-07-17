using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Presentation.Shell.Services;
using IIoT.Edge.Presentation.Shell.Views;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class ShellLoginDialogHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(LocalAdminCredentialStatus.NotConfigured, "LocalSetupPanel", "初始化并登录")]
    [InlineData(LocalAdminCredentialStatus.Invalid, "LocalSetupPanel", "初始化并登录")]
    [InlineData(LocalAdminCredentialStatus.RequiresPasswordReset, "LocalResetPanel", "重置并登录")]
    [InlineData(LocalAdminCredentialStatus.Ready, "LocalLoginPanel", "登录")]
    public void LocalEmergencyMode_ShouldMatchCredentialStatus(
        LocalAdminCredentialStatus status,
        string visiblePanelName,
        string submitText)
    {
        var window = new ShellLoginDialog(new StubShellAuthContext(status))
        {
            Width = 960,
            Height = 640
        };

        try
        {
            window.Show();

            Assert.Equal(visiblePanelName == "LocalLoginPanel", window.FindControl<Control>("LocalLoginPanel")?.IsVisible);
            Assert.Equal(visiblePanelName == "LocalSetupPanel", window.FindControl<Control>("LocalSetupPanel")?.IsVisible);
            Assert.Equal(visiblePanelName == "LocalResetPanel", window.FindControl<Control>("LocalResetPanel")?.IsVisible);
            Assert.Equal(submitText, window.FindControl<Button>("LoginButton")?.Content);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class StubShellAuthContext(LocalAdminCredentialStatus status) : IShellAuthContext
    {
        public bool IsAuthenticated => false;

        public UserSession? CurrentUser => null;

        public string OperatorName => "未登录";

        public bool HasCloudDeviceIdentity => false;

        public LocalAdminCredentialStatus LocalAdminCredentialStatus => status;

        public Task<AuthResult> LoginLocalEmergencyAsync(string? password)
            => Task.FromResult(AuthResult.Fail("not exercised"));

        public Task<AuthResult> InitializeLocalEmergencyAdminAsync(string? newPassword)
            => Task.FromResult(AuthResult.Fail("not exercised"));

        public Task<AuthResult> ResetLocalEmergencyPasswordAsync(string? currentPassword, string? newPassword)
            => Task.FromResult(AuthResult.Fail("not exercised"));

        public Task<AuthResult> LoginCloudEmployeeAsync(string? employeeNo, string? password)
            => Task.FromResult(AuthResult.Fail("not exercised"));

        public void Logout()
        {
        }
    }
}
