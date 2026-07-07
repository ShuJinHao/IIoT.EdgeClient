using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LocalLauncherAuthServiceTests
{
    private const string LegacySha256Password123456 = "8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92";

    [Fact]
    public void Authenticate_WhenPasswordMatches_ShouldSucceed()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.HashPassword("ChangeMe123!"),
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.Authenticate("edge-admin", "ChangeMe123!");

        Assert.True(result.Success);
        Assert.Equal("本地管理员", result.DisplayName);
    }

    [Fact]
    public void ChangePassword_WhenOldPasswordMatches_ShouldUpdateStoredHash()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.HashPassword("123456"),
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.ChangePassword("edge-admin", "123456", "NewPass123!");

        Assert.True(result.Success);
        Assert.False(service.Authenticate("edge-admin", "123456").Success);
        Assert.True(service.Authenticate("edge-admin", "NewPass123!").Success);
    }

    [Fact]
    public void ChangePassword_WhenNewPasswordIsEmpty_ShouldFail()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.HashPassword("123456"),
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.ChangePassword("edge-admin", "123456", "");

        Assert.False(result.Success);
        Assert.Contains("新密码", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangePassword_WhenNewPasswordDoesNotMeetPolicy_ShouldFail()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.HashPassword("123456"),
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.ChangePassword("edge-admin", "123456", "new-pass");

        Assert.False(result.Success);
        Assert.Equal(LauncherPasswordPolicy.RequirementMessage, result.ErrorMessage);
    }

    [Fact]
    public void Authenticate_WhenPasswordDoesNotMatch_ShouldFail()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.HashPassword("ChangeMe123!"),
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.Authenticate("edge-admin", "wrong-password");

        Assert.False(result.Success);
        Assert.Contains("不正确", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(1, accounts.LoadAccounts()[0].AccessFailedCount);
    }

    [Fact]
    public void Authenticate_WhenPasswordFailsFiveTimes_ShouldLockAccount()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.HashPassword("ChangeMe123!"),
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        for (var i = 0; i < 4; i++)
        {
            var failed = service.Authenticate("edge-admin", "wrong-password");
            Assert.False(failed.Success);
            Assert.Contains("不正确", failed.ErrorMessage, StringComparison.Ordinal);
        }

        var locked = service.Authenticate("edge-admin", "wrong-password");
        var stillLocked = service.Authenticate("edge-admin", "ChangeMe123!");

        Assert.False(locked.Success);
        Assert.Equal(LocalLauncherAuthService.AccountLockedError, locked.ErrorMessage);
        Assert.False(stillLocked.Success);
        Assert.Equal(LocalLauncherAuthService.AccountLockedError, stillLocked.ErrorMessage);
        Assert.Equal(5, accounts.LoadAccounts()[0].AccessFailedCount);
        Assert.NotNull(accounts.LoadAccounts()[0].LockoutUntilUtc);
    }

    [Fact]
    public void ChangePassword_WhenOldPasswordFailsFiveTimes_ShouldLockAccount()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.HashPassword("ChangeMe123!"),
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        for (var i = 0; i < 4; i++)
        {
            var failed = service.ChangePassword("edge-admin", "wrong-password", "NewPass123!");
            Assert.False(failed.Success);
            Assert.Contains("旧密码", failed.ErrorMessage, StringComparison.Ordinal);
        }

        var locked = service.ChangePassword("edge-admin", "wrong-password", "NewPass123!");

        Assert.False(locked.Success);
        Assert.Equal(LocalLauncherAuthService.AccountLockedError, locked.ErrorMessage);
        Assert.Equal(5, accounts.LoadAccounts()[0].AccessFailedCount);
        Assert.NotNull(accounts.LoadAccounts()[0].LockoutUntilUtc);
    }

    [Fact]
    public void Authenticate_WhenPasswordMatches_ShouldResetFailedAttemptState()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.HashPassword("ChangeMe123!"),
                true,
                AccessFailedCount: 2)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.Authenticate("edge-admin", "ChangeMe123!");

        Assert.True(result.Success);
        Assert.Equal(0, accounts.LoadAccounts()[0].AccessFailedCount);
        Assert.Null(accounts.LoadAccounts()[0].LockoutUntilUtc);
    }

    [Fact]
    public void Authenticate_WhenStoredHashIsLegacySha256_ShouldRequirePasswordReset()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LegacySha256Password123456,
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.Authenticate("edge-admin", "123456");

        Assert.False(result.Success);
        Assert.Equal(LocalLauncherAuthService.PasswordResetRequiredError, result.ErrorMessage);
    }

    [Fact]
    public void ChangePassword_WhenStoredHashIsLegacySha256AndOldPasswordMatches_ShouldWritePbkdf2Hash()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LegacySha256Password123456,
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.ChangePassword("edge-admin", "123456", "NewPass123!");

        Assert.True(result.Success);
        Assert.False(string.Equals(LegacySha256Password123456, accounts.LoadAccounts()[0].PasswordHash, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("pbkdf2-sha256$v1$", accounts.LoadAccounts()[0].PasswordHash, StringComparison.Ordinal);
        Assert.True(service.Authenticate("edge-admin", "NewPass123!").Success);
    }

    [Fact]
    public void Authenticate_WhenAccountCatalogCannotLoad_ShouldReturnConfigurationError()
    {
        var service = new LocalLauncherAuthService(
            new ThrowingLauncherAccountCatalog(new FileNotFoundException("missing accounts")));

        var result = service.Authenticate("edge-admin", "ChangeMe123!");

        Assert.False(result.Success);
        Assert.Equal(LocalLauncherAuthService.AccountConfigurationUnavailableError, result.ErrorMessage);
    }

    [Fact]
    public void ChangePassword_WhenAccountCatalogCannotWrite_ShouldReturnConfigurationError()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.HashPassword("123456"),
                true)
        ])
        {
            UpdatePasswordException = new UnauthorizedAccessException("read only")
        };
        var service = new LocalLauncherAuthService(accounts);

        var result = service.ChangePassword("edge-admin", "123456", "NewPass123!");

        Assert.False(result.Success);
        Assert.Equal(LocalLauncherAuthService.AccountConfigurationUnavailableError, result.ErrorMessage);
    }

    private sealed class StubLauncherAccountCatalog : ILauncherAccountCatalog
    {
        private readonly List<LauncherAccountRecord> _accounts;

        public StubLauncherAccountCatalog(IReadOnlyList<LauncherAccountRecord> accounts)
        {
            _accounts = accounts.ToList();
        }

        public Exception? UpdatePasswordException { get; init; }

        public IReadOnlyList<LauncherAccountRecord> LoadAccounts() => _accounts;

        public void UpdatePasswordHash(string userName, string passwordHash)
        {
            if (UpdatePasswordException is not null)
            {
                throw UpdatePasswordException;
            }

            var index = _accounts.FindIndex(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException("账号不存在。");
            }

            _accounts[index] = _accounts[index] with
            {
                PasswordHash = passwordHash,
                AccessFailedCount = 0,
                LockoutUntilUtc = null
            };
        }

        public void UpdateLoginSecurityState(string userName, int accessFailedCount, DateTimeOffset? lockoutUntilUtc)
        {
            var index = _accounts.FindIndex(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException("账号不存在。");
            }

            _accounts[index] = _accounts[index] with
            {
                AccessFailedCount = accessFailedCount,
                LockoutUntilUtc = lockoutUntilUtc
            };
        }
    }

    private sealed class ThrowingLauncherAccountCatalog(Exception loadException) : ILauncherAccountCatalog
    {
        public IReadOnlyList<LauncherAccountRecord> LoadAccounts()
            => throw loadException;

        public void UpdatePasswordHash(string userName, string passwordHash)
        {
            throw new InvalidOperationException("not available");
        }

        public void UpdateLoginSecurityState(string userName, int accessFailedCount, DateTimeOffset? lockoutUntilUtc)
        {
            throw new InvalidOperationException("not available");
        }
    }
}
