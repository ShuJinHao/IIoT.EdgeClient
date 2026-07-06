using System.Text.Json;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Launcher.Services;

public sealed class LocalLauncherAuthService : ILocalLauncherAuthService
{
    public const string AccountConfigurationUnavailableError = "本地账号配置不可用。";
    public const string PasswordResetRequiredError = "本地密码使用旧哈希格式，请先修改密码。";

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

        var accountResult = LoadAccount(userName);
        if (accountResult.ConfigurationError)
        {
            return LauncherAuthenticationResult.Failed(AccountConfigurationUnavailableError);
        }

        if (accountResult.Account is null || !accountResult.Account.IsEnabled)
        {
            return LauncherAuthenticationResult.Failed("本地账号不存在，或已被禁用。");
        }

        var verification = LauncherPasswordHasher.Verify(password, accountResult.Account.PasswordHash);
        if (verification == EdgePasswordVerificationResult.LegacySha256Verified)
        {
            return LauncherAuthenticationResult.Failed(PasswordResetRequiredError);
        }

        if (verification != EdgePasswordVerificationResult.Verified)
        {
            return LauncherAuthenticationResult.Failed("账号或密码不正确。");
        }

        return LauncherAuthenticationResult.Passed(accountResult.Account);
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

        if (string.IsNullOrWhiteSpace(userName))
        {
            return LauncherPasswordChangeResult.Failed("请输入账号。");
        }

        if (string.IsNullOrWhiteSpace(oldPassword))
        {
            return LauncherPasswordChangeResult.Failed("请输入密码。");
        }

        var accountResult = LoadAccount(userName);
        if (accountResult.ConfigurationError)
        {
            return LauncherPasswordChangeResult.Failed(AccountConfigurationUnavailableError);
        }

        if (accountResult.Account is null || !accountResult.Account.IsEnabled)
        {
            return LauncherPasswordChangeResult.Failed("本地账号不存在，或已被禁用。");
        }

        var verification = LauncherPasswordHasher.Verify(oldPassword, accountResult.Account.PasswordHash);
        if (verification is not EdgePasswordVerificationResult.Verified
            and not EdgePasswordVerificationResult.LegacySha256Verified)
        {
            return LauncherPasswordChangeResult.Failed("旧密码校验失败。");
        }

        try
        {
            _accountCatalog.UpdatePasswordHash(
                userName!.Trim(),
                LauncherPasswordHasher.HashPassword(newPassword));
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

    private AccountLoadResult LoadAccount(string userName)
    {
        try
        {
            var account = _accountCatalog.LoadAccounts()
                .FirstOrDefault(x => string.Equals(x.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
            return new AccountLoadResult(account, ConfigurationError: false);
        }
        catch (Exception ex) when (IsAccountConfigurationException(ex))
        {
            return new AccountLoadResult(null, ConfigurationError: true);
        }
    }

    private sealed record AccountLoadResult(LauncherAccountRecord? Account, bool ConfigurationError);
}
