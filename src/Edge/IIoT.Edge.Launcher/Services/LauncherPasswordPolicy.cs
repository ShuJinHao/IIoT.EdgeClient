namespace IIoT.Edge.Launcher.Services;

public static class LauncherPasswordPolicy
{
    public const int MinimumLength = 10;
    public const string RequirementMessage = "新密码至少需要 10 位，并包含大小写字母、数字和符号。";

    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "新密码不能为空。";
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
