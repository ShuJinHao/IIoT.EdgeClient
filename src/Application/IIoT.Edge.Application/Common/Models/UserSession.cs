namespace IIoT.Edge.Application.Common.Models
{
    /// <summary>
    /// 当前登录用户的会话信息。
    /// 由认证服务填充，供上层以只读方式使用。
    /// </summary>
    public class UserSession
    {
        /// <summary>
        /// 显示名称。
        /// 本地紧急管理员固定显示“紧急管理员”，云端登录显示真实姓名。
        /// </summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>
        /// 当前登录员工号；本地管理员模式使用固定标识。
        /// </summary>
        public string? EmployeeNo { get; init; }

        /// <summary>
        /// 是否为本地紧急管理员。
        /// </summary>
        public bool IsLocalAdmin { get; init; }

        /// <summary>
        /// 从 JWT 权限声明解析出的权限集合。
        /// 本地紧急管理员通常不依赖此字段，而是直接通过 <see cref="IsLocalAdmin"/> 放行。
        /// </summary>
        public HashSet<string> Permissions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 云端登录会话过期时间；本地管理员模式下为空。
        /// </summary>
        public DateTimeOffset? ExpiresAtUtc { get; init; }
    }
}
