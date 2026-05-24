using IIoT.Edge.Application.Common.Models;

namespace IIoT.Edge.Presentation.Shell.Services;

public interface IShellAuthContext
{
    UserSession? CurrentUser { get; }

    bool IsAuthenticated { get; }

    bool HasCloudDeviceIdentity { get; }

    Task<AuthResult> LoginLocalEmergencyAsync(string? password);

    Task<AuthResult> LoginCloudEmployeeAsync(string? employeeNo, string? password);

    void Logout();
}
