using System.Text.Json;
using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public sealed class LocalLauncherAuthService : ILocalLauncherAuthService
{
    public const string AccountConfigurationUnavailableError = "本地账号配置不可用。";

    private readonly ILauncherAccountCatalog _accountCatalog;

    public LocalLauncherAuthService(ILauncherAccountCatalog accountCatalog)
    {
        _accountCatalog = accountCatalog ?? throw new ArgumentNullException(nameof(accountCatalog));
    }

    public LauncherAuthenticationResult Authenticate(string? userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return LauncherAuthenticationResult.Failed("请输入账号。");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return LauncherAuthenticationResult.Failed("请输入密码。");
        }

        LauncherAccountRecord? account;
        try
        {
            account = _accountCatalog.LoadAccounts()
                .FirstOrDefault(x => string.Equals(x.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (IsAccountConfigurationException(ex))
        {
            return LauncherAuthenticationResult.Failed(AccountConfigurationUnavailableError);
        }

        if (account is null || !account.IsEnabled)
        {
            return LauncherAuthenticationResult.Failed("本地账号不存在，或已被禁用。");
        }

        if (!LauncherPasswordHasher.Verify(password, account.PasswordHash))
        {
            return LauncherAuthenticationResult.Failed("账号或密码不正确。");
        }

        return LauncherAuthenticationResult.Passed(account);
    }

    public LauncherPasswordChangeResult ChangePassword(
        string? userName,
        string? oldPassword,
        string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return LauncherPasswordChangeResult.Failed("新密码不能为空。");
        }

        if (newPassword.Length < 6)
        {
            return LauncherPasswordChangeResult.Failed("新密码至少需要 6 位。");
        }

        var authentication = Authenticate(userName, oldPassword);
        if (!authentication.Success)
        {
            return LauncherPasswordChangeResult.Failed(authentication.ErrorMessage ?? "旧密码校验失败。");
        }

        try
        {
            _accountCatalog.UpdatePasswordHash(
                userName!.Trim(),
                LauncherPasswordHasher.ComputeSha256(newPassword));
            return LauncherPasswordChangeResult.Passed();
        }
        catch (Exception ex) when (IsAccountConfigurationException(ex))
        {
            return LauncherPasswordChangeResult.Failed(AccountConfigurationUnavailableError);
        }
    }

    private static bool IsAccountConfigurationException(Exception ex)
        => ex is FileNotFoundException
            or DirectoryNotFoundException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or JsonException;
}
