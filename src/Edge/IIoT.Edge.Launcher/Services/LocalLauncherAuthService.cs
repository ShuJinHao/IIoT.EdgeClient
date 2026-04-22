using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public sealed class LocalLauncherAuthService : ILocalLauncherAuthService
{
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

        var account = _accountCatalog.LoadAccounts()
            .FirstOrDefault(x => string.Equals(x.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (account is null || !account.IsEnabled)
        {
            return LauncherAuthenticationResult.Failed("本地账号不存在，或已被禁用。");
        }

        if (!LauncherPasswordHasher.Verify(password, account.PasswordHash))
        {
            return LauncherAuthenticationResult.Failed("账号或密码不正确。");
        }

        return LauncherAuthenticationResult.Passed(account.DisplayName);
    }
}
