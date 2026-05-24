namespace IIoT.Edge.Application.Auth.LocalAccounts;

public sealed class LocalAccountAuthService : ILocalAccountAuthService
{
    private readonly ILocalAccountCatalog _accountCatalog;

    public LocalAccountAuthService(ILocalAccountCatalog accountCatalog)
    {
        _accountCatalog = accountCatalog ?? throw new ArgumentNullException(nameof(accountCatalog));
    }

    public LocalAccountAuthenticationResult Authenticate(string? userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return LocalAccountAuthenticationResult.Failed("请输入账号。");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return LocalAccountAuthenticationResult.Failed("请输入密码。");
        }

        var account = _accountCatalog.LoadAccounts()
            .FirstOrDefault(x => string.Equals(x.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (account is null || !account.IsEnabled)
        {
            return LocalAccountAuthenticationResult.Failed("本地账号不存在，或已被禁用。");
        }

        if (!LocalAccountPasswordHasher.Verify(password, account.PasswordHash))
        {
            return LocalAccountAuthenticationResult.Failed("账号或密码不正确。");
        }

        return LocalAccountAuthenticationResult.Passed(account);
    }

    public LocalAccountPasswordChangeResult ChangePassword(string? userName, string? oldPassword, string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return LocalAccountPasswordChangeResult.Failed("新密码不能为空。");
        }

        if (newPassword.Length < 6)
        {
            return LocalAccountPasswordChangeResult.Failed("新密码至少需要 6 位。");
        }

        var authentication = Authenticate(userName, oldPassword);
        if (!authentication.Success)
        {
            return LocalAccountPasswordChangeResult.Failed(authentication.ErrorMessage ?? "旧密码校验失败。");
        }

        _accountCatalog.UpdatePasswordHash(
            userName!.Trim(),
            LocalAccountPasswordHasher.ComputeSha256(newPassword));
        return LocalAccountPasswordChangeResult.Passed();
    }
}
