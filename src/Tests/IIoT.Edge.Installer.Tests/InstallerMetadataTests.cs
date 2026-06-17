using System.Reflection;
using IIoT.Edge.Installer;
using Xunit;

namespace IIoT.Edge.Installer.Tests;

public sealed class InstallerMetadataTests
{
    [Fact]
    public void InstallerAssembly_ShouldExposeWindowsVersionInfoMetadata()
    {
        var assembly = typeof(InstallerService).Assembly;

        Assert.Equal("IIoT", assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Assert.Equal("IIoT Edge Client", assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
        Assert.Equal("IIoT Edge Client", assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description);
        Assert.Equal("1.0.0.0", assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
        Assert.Equal("1.0.0-dev", assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }
}
