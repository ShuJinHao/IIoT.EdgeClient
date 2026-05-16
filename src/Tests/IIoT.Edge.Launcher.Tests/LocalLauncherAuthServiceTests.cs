using IIoT.Edge.Launcher;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LocalLauncherAuthServiceTests
{
    [Fact]
    public void Authenticate_WhenPasswordMatches_ShouldSucceed()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                new Pbkdf2PasswordHasher().HashPassword("ChangeMe123!"),
                true)
        ]);
        var service = CreateService(accounts);

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
                new Pbkdf2PasswordHasher().HashPassword("123456"),
                true)
        ]);
        var service = CreateService(accounts);

        var result = service.ChangePassword("edge-admin", "123456", "new-pass");

        Assert.True(result.Success);
        Assert.False(service.Authenticate("edge-admin", "123456").Success);
        Assert.True(service.Authenticate("edge-admin", "new-pass").Success);
        Assert.StartsWith(
            "PBKDF2-SHA256$600000$",
            Assert.Single(accounts.LoadAccounts()).PasswordHash,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChangePassword_WhenNewPasswordIsEmpty_ShouldFail()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                new Pbkdf2PasswordHasher().HashPassword("123456"),
                true)
        ]);
        var service = CreateService(accounts);

        var result = service.ChangePassword("edge-admin", "123456", "");

        Assert.False(result.Success);
        Assert.Contains("新密码", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticate_WhenPasswordDoesNotMatch_ShouldFail()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                new Pbkdf2PasswordHasher().HashPassword("ChangeMe123!"),
                true)
        ]);
        var service = CreateService(accounts);

        var result = service.Authenticate("edge-admin", "wrong-password");

        Assert.False(result.Success);
        Assert.Contains("不正确", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void HashPassword_ShouldUseRandomSaltAndVerifyBothHashes()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var first = hasher.HashPassword("ChangeMe123!");
        var second = hasher.HashPassword("ChangeMe123!");

        Assert.NotEqual(first, second);
        Assert.StartsWith("PBKDF2-SHA256$600000$", first, StringComparison.Ordinal);
        Assert.True(hasher.VerifyPassword("ChangeMe123!", first).Success);
        Assert.True(hasher.VerifyPassword("ChangeMe123!", second).Success);
        Assert.False(hasher.VerifyPassword("wrong-password", first).Success);
    }

    [Fact]
    public void Authenticate_WhenLegacySha256Matches_ShouldRehashAccount()
    {
        const string legacySha256 = "8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92";
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord("edge-admin", "admin", legacySha256, true)
        ]);
        var service = CreateService(accounts);

        var result = service.Authenticate("edge-admin", "123456");

        Assert.True(result.Success);
        var account = Assert.Single(accounts.LoadAccounts());
        Assert.NotEqual(legacySha256, account.PasswordHash);
        Assert.StartsWith("PBKDF2-SHA256$600000$", account.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticate_WhenLegacySha256DoesNotMatch_ShouldNotRehashAccount()
    {
        const string legacySha256 = "8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92";
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord("edge-admin", "admin", legacySha256, true)
        ]);
        var service = CreateService(accounts);

        var result = service.Authenticate("edge-admin", "wrong-password");

        Assert.False(result.Success);
        var account = Assert.Single(accounts.LoadAccounts());
        Assert.Equal(legacySha256, account.PasswordHash);
    }

    [Fact]
    public void AddLauncherServices_ShouldResolveHasherAndAuthService()
    {
        using var provider = new ServiceCollection()
            .AddLauncherServices(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
            .BuildServiceProvider();

        Assert.IsType<Pbkdf2PasswordHasher>(provider.GetRequiredService<IPasswordHasher>());
        Assert.IsType<LocalLauncherAuthService>(provider.GetRequiredService<ILocalLauncherAuthService>());
    }

    private static LocalLauncherAuthService CreateService(ILauncherAccountCatalog accounts)
        => new(accounts, new Pbkdf2PasswordHasher());

    private sealed class StubLauncherAccountCatalog : ILauncherAccountCatalog
    {
        private readonly List<LauncherAccountRecord> _accounts;

        public StubLauncherAccountCatalog(IReadOnlyList<LauncherAccountRecord> accounts)
        {
            _accounts = accounts.ToList();
        }

        public IReadOnlyList<LauncherAccountRecord> LoadAccounts() => _accounts;

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
