using IIoT.Edge.Launcher.Services;
using System.Text;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherProfileCatalogTests
{
    [Fact]
    public void LoadProfiles_ShouldResolveRelativePathsAndApplyDefaults()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "launcher.profiles.json"),
                """
                [
                  {
                    "ProfileId": "StackingLine",
                    "DisplayName": "叠片",
                    "Description": "Stacking profile",
                    "MachineProfile": "StackingLine"
                  }
                ]
                """);

            var catalog = new LauncherProfileCatalog(tempDirectory);

            var profile = Assert.Single(catalog.LoadProfiles());
            Assert.Equal(Path.Combine(tempDirectory, "IIoT.Edge.Shell.exe"), profile.ExecutablePath);
            Assert.Null(profile.ImagePath);
            Assert.Equal("StackingLine", profile.MachineProfile);
            Assert.Equal("Cog", profile.IconKind);
            Assert.Equal("#0F766E", profile.AccentColor);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void LoadProfiles_ShouldResolveSiblingRuntimeExecutable()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "launcher.profiles.json"),
                """
                [
                  {
                    "ProfileId": "HomogenizationLine",
                    "DisplayName": "匀浆",
                    "Description": "Homogenization profile",
                    "ImagePath": "Assets/Profiles/homogenization.png",
                    "IconKind": "BeakerOutline",
                    "AccentColor": "#4D7C0F",
                    "MachineProfile": "HomogenizationLine",
                    "ExecutablePath": "..\\homogenization\\IIoT.Edge.Shell.exe"
                  }
                ]
                """);

            var catalog = new LauncherProfileCatalog(tempDirectory);

            var profile = Assert.Single(catalog.LoadProfiles());
            Assert.Equal(
                Path.GetFullPath(Path.Combine(tempDirectory, @"..\homogenization\IIoT.Edge.Shell.exe")),
                profile.ExecutablePath);
            Assert.Equal(Path.Combine(tempDirectory, "Assets", "Profiles", "homogenization.png"), profile.ImagePath);
            Assert.Equal("BeakerOutline", profile.IconKind);
            Assert.Equal("#4D7C0F", profile.AccentColor);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void LoadProfiles_WhenRequiredFieldIsMissing_ShouldThrow()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "launcher.profiles.json"),
                """
                [
                  {
                    "ProfileId": "Broken",
                    "DisplayName": "Broken"
                  }
                ]
                """);

            var catalog = new LauncherProfileCatalog(tempDirectory);

            var ex = Assert.Throws<InvalidOperationException>(() => catalog.LoadProfiles());
            Assert.Contains("MachineProfile", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-launcher-profile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteText(string path, string content)
        => File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
