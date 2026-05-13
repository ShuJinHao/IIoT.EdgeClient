namespace IIoT.Edge.Application.Abstractions.Auth;

/// <summary>
/// 客户端权限消费门面。
/// 只负责消费当前会话里的现有权限结果，不推导额外业务规则。
/// </summary>
public interface IClientPermissionService
{
    bool CanEditParams { get; }

    bool CanEditHardware { get; }

    bool IsLocalAdmin { get; }

    bool HasPermission(string permission);

    event Action? PermissionStateChanged;
}
