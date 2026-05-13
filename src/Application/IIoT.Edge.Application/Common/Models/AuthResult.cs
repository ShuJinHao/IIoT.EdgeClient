namespace IIoT.Edge.Application.Common.Models
{
    /// <summary>
    /// 登录操作结果。
    /// </summary>
    public class AuthResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// 创建登录成功结果。
        /// </summary>
        public static AuthResult Ok(string message = "登录成功")
            => new() { Success = true, Message = message };

        /// <summary>
        /// 创建登录失败结果。
        /// </summary>
        public static AuthResult Fail(string message)
            => new() { Success = false, Message = message };
    }
}
