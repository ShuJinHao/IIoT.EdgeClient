using IIoT.Edge.Shell.Core;
using System.Text;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class ShellConfigurationLoaderBehaviorTests
{
    [Fact]
    public void Load_WhenMachineProfileExists_ShouldApplyProfileOverrides()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Shell": {
                    "MachineProfile": "StackingLine"
                  },
                  "Modules": {
                    "Enabled": [ "Injection" ]
                  }
                }
                """);
            WriteText(
                Path.Combine(tempDirectory, "appsettings.machine.StackingLine.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "Stacking" ]
                  }
                }
                """);

            var result = ShellConfigurationLoader.Load(tempDirectory);

            Assert.Equal("StackingLine", result.MachineProfile);
            Assert.True(result.IsMachineProfileLoaded);
            Assert.Equal("Stacking", result.Configuration["Modules:Enabled:0"]);
            Assert.Equal("True", result.Configuration["Shell:MachineProfileLoaded"]);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenMachineProfileFileIsMissing_ShouldKeepBaseSettingsAndExposeMetadata()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Shell": {
                    "MachineProfile": "MissingLine"
                  },
                  "Modules": {
                    "Enabled": [ "Injection" ]
                  }
                }
                """);

            var result = ShellConfigurationLoader.Load(tempDirectory);

            Assert.Equal("MissingLine", result.MachineProfile);
            Assert.False(result.IsMachineProfileLoaded);
            Assert.Equal("Injection", result.Configuration["Modules:Enabled:0"]);
            Assert.Equal("False", result.Configuration["Shell:MachineProfileLoaded"]);
            Assert.Equal("appsettings.machine.MissingLine.json", result.Configuration["Shell:MachineProfileFileName"]);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-shell-config-tests", Guid.NewGuid().ToString("N"));
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
