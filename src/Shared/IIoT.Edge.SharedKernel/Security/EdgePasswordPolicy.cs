namespace IIoT.Edge.SharedKernel.Security;

public static class EdgePasswordPolicy
{
    public const int MinimumLength = 10;
    public const string EmptyMessage = "新密码不能为空。";
    public const string RequirementMessage = "新密码至少需要 10 位，并包含大小写字母、数字和符号。";

    public static string? ValidateNewPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return EmptyMessage;
        }

        if (password.Length < MinimumLength
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(static c => !char.IsLetterOrDigit(c)))
        {
            return RequirementMessage;
        }

        return null;
    }
}
