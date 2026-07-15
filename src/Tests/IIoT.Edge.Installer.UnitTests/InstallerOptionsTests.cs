using IIoT.Edge.Installer;

namespace IIoT.Edge.Installer.UnitTests;

public sealed class InstallerOptionsTests
{
    [Fact]
    public void Parse_ShouldReadVelopackInstallDirectoryAndSilentMode()
    {
        var options = InstallerOptions.Parse([
            "--silent",
            "--installto",
            @"D:\IIoT\EdgeClient",
            "--no-launch"
        ]);

        Assert.Equal(@"D:\IIoT\EdgeClient", options.InstallTo);
        Assert.True(options.Silent);
        Assert.True(options.NoLaunch);
    }
}
