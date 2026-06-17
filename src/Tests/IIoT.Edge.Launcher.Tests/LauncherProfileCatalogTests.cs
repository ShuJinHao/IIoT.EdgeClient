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
                    "ProfileId": "HomogenizationLine",
                    "DisplayName": "匀浆",
                    "Description": "Homogenization profile",
                    "MachineProfile": "HomogenizationLine"
                  }
                ]
                """);

            var catalog = new LauncherProfileCatalog(tempDirectory);

            var profile = Assert.Single(catalog.LoadProfiles());
            Assert.Equal(
                Path.GetFullPath(Path.Combine(tempDirectory, "..", "host", "IIoT.Edge.Shell")),
                profile.ExecutablePath);
            Assert.Null(profile.ImagePath);
            Assert.Equal("HomogenizationLine", profile.MachineProfile);
            Assert.Equal("Cog", profile.IconKind);
            Assert.Equal("#0F766E", profile.AccentColor);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void LoadProfiles_ShouldResolveSiblingHostExecutable()
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
                    "ExecutablePath": "..\\host\\IIoT.Edge.Shell"
                  }
                ]
                """);

            var catalog = new LauncherProfileCatalog(tempDirectory);

            var profile = Assert.Single(catalog.LoadProfiles());
            Assert.Equal(
                Path.GetFullPath(Path.Combine(tempDirectory, "..", "host", "IIoT.Edge.Shell")),
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
    public void LoadProfiles_WhenRequiredFieldIsMissing_ShouldThrowChineseError()
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
            Assert.Contains("启动器工序", ex.Message, StringComparison.Ordinal);
            Assert.Contains("MachineProfile", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void LoadProfiles_WhenProfileIdIsMissing_ShouldThrowChineseError()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "launcher.profiles.json"),
                """
                [
                  {
                    "DisplayName": "匀浆",
                    "MachineProfile": "HomogenizationLine"
                  }
                ]
                """);

            var catalog = new LauncherProfileCatalog(tempDirectory);

            var ex = Assert.Throws<InvalidOperationException>(() => catalog.LoadProfiles());
            Assert.Contains("缺少 ProfileId", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void LoadProfiles_WhenCatalogIsMissing_ShouldThrowChineseError()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var catalog = new LauncherProfileCatalog(tempDirectory);

            var ex = Assert.Throws<FileNotFoundException>(() => catalog.LoadProfiles());
            Assert.Contains("未找到启动器工序清单", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void SourceProfileCatalog_ShouldLoadHomogenizationProfile()
    {
        var repoRoot = FindRepoRoot();
        var catalogPath = Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Launcher", "launcher.profiles.json");
        var catalog = new LauncherProfileCatalog(Path.GetDirectoryName(catalogPath)!, Path.GetFileName(catalogPath));

        var profile = Assert.Single(catalog.LoadProfiles());
        Assert.Equal("HomogenizationLine", profile.ProfileId);
        Assert.Equal("匀浆", profile.DisplayName);
        Assert.Equal("HomogenizationLine", profile.MachineProfile);
        Assert.EndsWith(
            Path.Combine("host", "IIoT.Edge.Shell"),
            profile.ExecutablePath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 IIoT.EdgeClient 仓库根目录。");
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
