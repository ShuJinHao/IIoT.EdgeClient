using IIoT.Edge.Application.Abstractions.Auth;

namespace IIoT.Edge.Presentation.Shell.Services;

public interface IShellAuthContext
{
    UserSession? CurrentUser { get; }

    bool IsAuthenticated { get; }

    bool HasCloudDeviceIdentity { get; }

    LocalAdminCredentialStatus LocalAdminCredentialStatus { get; }

    Task<AuthResult> LoginLocalEmergencyAsync(string? password);

    Task<AuthResult> InitializeLocalEmergencyAdminAsync(string? newPassword);

    Task<AuthResult> ResetLocalEmergencyPasswordAsync(string? currentPassword, string? newPassword);

    Task<AuthResult> LoginCloudEmployeeAsync(string? employeeNo, string? password);

    void Logout();
}
