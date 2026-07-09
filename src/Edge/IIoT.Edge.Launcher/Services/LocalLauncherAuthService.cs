using System.Text.Json;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Launcher.Services;

public sealed class LocalLauncherAuthService : ILocalLauncherAuthService
{
    public const string AccountConfigurationUnavailableError = "本地账号配置不可用。";
    public const string AccountSetupUnavailableError = "本地账号当前不可初始化。";
    public const string PasswordResetRequiredError = "本地密码使用旧哈希格式，请先修改密码。";
    public const string AccountLockedError = "本地账号已临时锁定，请稍后再试。";
    public const string DisplayNameRequiredError = "请输入显示名称。";
    public const string PasswordConfirmationMismatchError = "两次输入的新密码不一致。";
    private const int MaxFailedAccessAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly ILauncherAccountCatalog _accountCatalog;
    private readonly TimeProvider _timeProvider;

    public LocalLauncherAuthService(ILauncherAccountCatalog accountCatalog, TimeProvider? timeProvider = null)
    {
        _accountCatalog = accountCatalog ?? throw new ArgumentNullException(nameof(accountCatalog));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public LauncherAccountCatalogStatus AccountCatalogStatus => _accountCatalog.GetCatalogStatus();

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

        var now = _timeProvider.GetUtcNow();
        if (IsLocked(accountResult.Account, now))
        {
            return LauncherAuthenticationResult.Failed(AccountLockedError);
        }

        var verification = LauncherPasswordHasher.Verify(password, accountResult.Account.PasswordHash);
        if (verification == EdgePasswordVerificationResult.LegacySha256Verified)
        {
            return LauncherAuthenticationResult.Failed(PasswordResetRequiredError);
        }

        if (verification != EdgePasswordVerificationResult.Verified)
        {
            return RegisterFailedLogin(accountResult.Account, now)
                ? LauncherAuthenticationResult.Failed(AccountLockedError)
                : LauncherAuthenticationResult.Failed("账号或密码不正确。");
        }

        ResetFailedLoginStateIfNeeded(accountResult.Account);
        return LauncherAuthenticationResult.Passed(accountResult.Account);
    }

    public LauncherAccountSetupResult InitializeLocalAccount(
        string? userName,
        string? displayName,
        string? newPassword,
        string? confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return LauncherAccountSetupResult.Failed("请输入账号。");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return LauncherAccountSetupResult.Failed(DisplayNameRequiredError);
        }

        var passwordPolicyError = LauncherPasswordPolicy.Validate(newPassword);
        if (passwordPolicyError is not null)
        {
            return LauncherAccountSetupResult.Failed(passwordPolicyError);
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return LauncherAccountSetupResult.Failed(PasswordConfirmationMismatchError);
        }

        try
        {
            var status = _accountCatalog.GetCatalogStatus();
            if (status is not LauncherAccountCatalogStatus.Missing
                and not LauncherAccountCatalogStatus.Empty
                and not LauncherAccountCatalogStatus.NeedsInitialSetup)
            {
                return LauncherAccountSetupResult.Failed(AccountSetupUnavailableError);
            }

            var account = new LauncherAccountRecord(
                userName.Trim(),
                displayName.Trim(),
                LauncherPasswordHasher.HashPassword(newPassword!),
                IsEnabled: true);
            _accountCatalog.InitializeAccount(account.UserName, account.DisplayName, account.PasswordHash);
            return LauncherAccountSetupResult.Passed(account);
        }
        catch (Exception ex) when (IsAccountConfigurationException(ex))
        {
            return LauncherAccountSetupResult.Failed(AccountConfigurationUnavailableError);
        }
    }

    public LauncherPasswordChangeResult ChangePassword(
        string? userName,
        string? oldPassword,
        string? newPassword)
    {
        var passwordPolicyError = LauncherPasswordPolicy.Validate(newPassword);
        if (passwordPolicyError is not null)
        {
            return LauncherPasswordChangeResult.Failed(passwordPolicyError);
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

        var now = _timeProvider.GetUtcNow();
        if (IsLocked(accountResult.Account, now))
        {
            return LauncherPasswordChangeResult.Failed(AccountLockedError);
        }

        var verification = LauncherPasswordHasher.Verify(oldPassword, accountResult.Account.PasswordHash);
        if (verification is not EdgePasswordVerificationResult.Verified
            and not EdgePasswordVerificationResult.LegacySha256Verified)
        {
            return RegisterFailedLogin(accountResult.Account, now)
                ? LauncherPasswordChangeResult.Failed(AccountLockedError)
                : LauncherPasswordChangeResult.Failed("旧密码校验失败。");
        }

        try
        {
            _accountCatalog.UpdatePasswordHash(
                userName!.Trim(),
                LauncherPasswordHasher.HashPassword(newPassword!));
            return LauncherPasswordChangeResult.Passed();
        }
        catch (Exception ex) when (IsAccountConfigurationException(ex))
        {
            return LauncherPasswordChangeResult.Failed(AccountConfigurationUnavailableError);
        }
    }

    private static bool IsLocked(LauncherAccountRecord account, DateTimeOffset now)
        => account.LockoutUntilUtc.HasValue && account.LockoutUntilUtc.Value > now;

    private bool RegisterFailedLogin(LauncherAccountRecord account, DateTimeOffset now)
    {
        var currentFailedCount = account.LockoutUntilUtc.HasValue && account.LockoutUntilUtc.Value <= now
            ? 0
            : Math.Max(0, account.AccessFailedCount);
        var nextFailedCount = currentFailedCount + 1;
        var lockoutUntil = nextFailedCount >= MaxFailedAccessAttempts
            ? now.Add(LockoutDuration)
            : (DateTimeOffset?)null;

        try
        {
            _accountCatalog.UpdateLoginSecurityState(account.UserName, nextFailedCount, lockoutUntil);
        }
        catch (Exception ex) when (IsAccountConfigurationException(ex))
        {
            return false;
        }

        return lockoutUntil.HasValue;
    }

    private void ResetFailedLoginStateIfNeeded(LauncherAccountRecord account)
    {
        if (account.AccessFailedCount <= 0 && account.LockoutUntilUtc is null)
        {
            return;
        }

        try
        {
            _accountCatalog.UpdateLoginSecurityState(account.UserName, 0, null);
        }
        catch (Exception ex) when (IsAccountConfigurationException(ex))
        {
            // Successful login must not be blocked only because clearing stale lockout metadata failed.
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
