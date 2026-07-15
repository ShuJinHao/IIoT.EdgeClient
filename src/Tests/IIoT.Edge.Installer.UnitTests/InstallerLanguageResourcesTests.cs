using System.Globalization;
using Xunit;

namespace IIoT.Edge.Installer.UnitTests;

public sealed class InstallerLanguageResourcesTests
{
    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("en-US", "en-US")]
    [InlineData("fr-FR", "en-US")]
    public void ResolveCultureName_ShouldMapSupportedInstallerLanguages(
        string cultureName,
        string expectedCultureName)
    {
        var resolved = InstallerLanguageResources.ResolveCultureName(
            CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expectedCultureName, resolved);
    }

    [Fact]
    public void BuildLanguageResourceUri_ShouldPointToInstallerAssemblyResources()
    {
        var uri = InstallerLanguageResources.BuildLanguageResourceUri("en-US");
        var installerAssemblyName = typeof(App).Assembly.GetName().Name;

        Assert.Equal(
            $"avares://{installerAssemblyName}/Resources/Languages/en-US.axaml",
            uri.ToString(),
            ignoreCase: true);
    }
}
