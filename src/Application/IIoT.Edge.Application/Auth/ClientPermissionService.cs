using IIoT.Edge.Application.Abstractions.Auth;

namespace IIoT.Edge.Application.Auth;

/// <summary>
/// 统一包装当前登录会话中的权限结果，供客户端界面和应用层复用。
/// </summary>
public sealed class ClientPermissionService : IClientPermissionService
{
    private readonly IAuthService _authService;

    public ClientPermissionService(IAuthService authService)
    {
        _authService = authService;
        _authService.AuthStateChanged += _ => PermissionStateChanged?.Invoke();
    }

    public bool CanEditParams => HasPermission(Permissions.ParamConfig);

    public bool CanEditHardware => HasPermission(Permissions.HardwareConfig);

    public bool IsLocalAdmin => _authService.CurrentUser?.IsLocalAdmin ?? false;

    public bool HasPermission(string permission) => _authService.HasPermission(permission);

    public event Action? PermissionStateChanged;
}
