using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
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
                LauncherPasswordHasher.ComputeSha256("ChangeMe123!"),
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.Authenticate("edge-admin", "ChangeMe123!");

        Assert.True(result.Success);
        Assert.Equal("本地管理员", result.DisplayName);
    }

    [Fact]
    public void Authenticate_WhenPasswordDoesNotMatch_ShouldFail()
    {
        var accounts = new StubLauncherAccountCatalog(
        [
            new LauncherAccountRecord(
                "edge-admin",
                "本地管理员",
                LauncherPasswordHasher.ComputeSha256("ChangeMe123!"),
                true)
        ]);
        var service = new LocalLauncherAuthService(accounts);

        var result = service.Authenticate("edge-admin", "wrong-password");

        Assert.False(result.Success);
        Assert.Contains("不正确", result.ErrorMessage, StringComparison.Ordinal);
    }

    private sealed class StubLauncherAccountCatalog : ILauncherAccountCatalog
    {
        private readonly IReadOnlyList<LauncherAccountRecord> _accounts;

        public StubLauncherAccountCatalog(IReadOnlyList<LauncherAccountRecord> accounts)
        {
            _accounts = accounts;
        }

        public IReadOnlyList<LauncherAccountRecord> LoadAccounts() => _accounts;
    }
}
