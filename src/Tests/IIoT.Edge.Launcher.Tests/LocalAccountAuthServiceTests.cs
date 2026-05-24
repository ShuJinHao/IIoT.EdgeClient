using IIoT.Edge.Application.Auth.LocalAccounts;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LocalAccountAuthServiceTests
{
    [Fact]
    public void Authenticate_WhenPasswordMatches_ShouldSucceed()
    {
        var accounts = new StubLocalAccountCatalog(
        [
            new LocalAccountRecord(
                "edge-admin",
                "本地管理员",
                LocalAccountPasswordHasher.ComputeSha256("ChangeMe123!"),
                true)
        ]);
        var service = new LocalAccountAuthService(accounts);

        var result = service.Authenticate("edge-admin", "ChangeMe123!");

        Assert.True(result.Success);
        Assert.Equal("本地管理员", result.DisplayName);
    }

    [Fact]
    public void ChangePassword_WhenOldPasswordMatches_ShouldUpdateStoredHash()
    {
        var accounts = new StubLocalAccountCatalog(
        [
            new LocalAccountRecord(
                "edge-admin",
                "本地管理员",
                LocalAccountPasswordHasher.ComputeSha256("123456"),
                true)
        ]);
        var service = new LocalAccountAuthService(accounts);

        var result = service.ChangePassword("edge-admin", "123456", "new-pass");

        Assert.True(result.Success);
        Assert.False(service.Authenticate("edge-admin", "123456").Success);
        Assert.True(service.Authenticate("edge-admin", "new-pass").Success);
    }

    [Fact]
    public void ChangePassword_WhenNewPasswordIsEmpty_ShouldFail()
    {
        var accounts = new StubLocalAccountCatalog(
        [
            new LocalAccountRecord(
                "edge-admin",
                "本地管理员",
                LocalAccountPasswordHasher.ComputeSha256("123456"),
                true)
        ]);
        var service = new LocalAccountAuthService(accounts);

        var result = service.ChangePassword("edge-admin", "123456", "");

        Assert.False(result.Success);
        Assert.Contains("新密码", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticate_WhenPasswordDoesNotMatch_ShouldFail()
    {
        var accounts = new StubLocalAccountCatalog(
        [
            new LocalAccountRecord(
                "edge-admin",
                "本地管理员",
                LocalAccountPasswordHasher.ComputeSha256("ChangeMe123!"),
                true)
        ]);
        var service = new LocalAccountAuthService(accounts);

        var result = service.Authenticate("edge-admin", "wrong-password");

        Assert.False(result.Success);
        Assert.Contains("不正确", result.ErrorMessage, StringComparison.Ordinal);
    }

    private sealed class StubLocalAccountCatalog : ILocalAccountCatalog
    {
        private readonly List<LocalAccountRecord> _accounts;

        public StubLocalAccountCatalog(IReadOnlyList<LocalAccountRecord> accounts)
        {
            _accounts = accounts.ToList();
        }

        public IReadOnlyList<LocalAccountRecord> LoadAccounts() => _accounts;

        public void UpdatePasswordHash(string userName, string passwordHash)
        {
            var index = _accounts.FindIndex(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException("账号不存在。");
            }

            _accounts[index] = _accounts[index] with { PasswordHash = passwordHash };
        }
    }
}
