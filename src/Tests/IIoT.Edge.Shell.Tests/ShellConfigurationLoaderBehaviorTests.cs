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
                    "MachineProfile": "HomogenizationLine"
                  },
                  "Modules": {
                    "Enabled": [ "Homogenization" ]
                  }
                }
                """);
            WriteText(
                Path.Combine(tempDirectory, "appsettings.machine.HomogenizationLine.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "Homogenization" ]
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(tempDirectory);

            Assert.Equal("HomogenizationLine", result.MachineProfile);
            Assert.True(result.IsMachineProfileLoaded);
            Assert.Equal("Homogenization", result.Configuration["Modules:Enabled:0"]);
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
                    "Enabled": [ "Homogenization" ]
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(tempDirectory);

            Assert.Equal("MissingLine", result.MachineProfile);
            Assert.False(result.IsMachineProfileLoaded);
            Assert.Equal("Homogenization", result.Configuration["Modules:Enabled:0"]);
            Assert.Equal("False", result.Configuration["Shell:MachineProfileLoaded"]);
            Assert.Equal("appsettings.machine.MissingLine.json", result.Configuration["Shell:MachineProfileFileName"]);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenMachineProfileComesFromEnvironmentVariable_ShouldPreferEnvironmentOverride()
    {
        var tempDirectory = CreateTempDirectory();
        const string environmentVariable = "Shell__MachineProfile";
        var originalValue = Environment.GetEnvironmentVariable(environmentVariable);

        try
        {
            WriteText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Shell": {
                    "MachineProfile": "HomogenizationLine"
                  }
                }
                """);
            WriteText(
                Path.Combine(tempDirectory, "appsettings.machine.HomogenizationLine.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "Homogenization" ]
                  }
                }
                """);

            Environment.SetEnvironmentVariable(environmentVariable, "HomogenizationLine");

            var result = new ShellConfigurationLoader().Load(tempDirectory);

            Assert.Equal("HomogenizationLine", result.MachineProfile);
            Assert.True(result.IsMachineProfileLoaded);
            Assert.Equal("Homogenization", result.Configuration["Modules:Enabled:0"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, originalValue);
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenPluginDefaultAndAppSettingsProvideSameModuleKey_ShouldPreferAppSettings()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Modules": {
                    "Homogenization": {
                      "Module": {
                        "Presentation": {
                          "MaxOutboundRecords": 25
                        }
                      }
                    }
                  }
                }
                """);
            var pluginConfigDirectory = Path.Combine(tempDirectory, "Modules", "Homogenization", "Config");
            Directory.CreateDirectory(pluginConfigDirectory);
            WriteText(
                Path.Combine(pluginConfigDirectory, "homogenization.module.json"),
                """
                {
                  "Modules": {
                    "Homogenization": {
                      "Module": {
                        "Presentation": {
                          "MaxOutboundRecords": 500
                        }
                      }
                    }
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(tempDirectory);

            Assert.Equal("25", result.Configuration["Modules:Homogenization:Module:Presentation:MaxOutboundRecords"]);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenPluginDefaultIsAtPublishedModuleRoot_ShouldApplyPluginDefaults()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "Homogenization" ]
                  }
                }
                """);
            var pluginDirectory = Path.Combine(tempDirectory, "Modules", "Homogenization");
            Directory.CreateDirectory(pluginDirectory);
            WriteText(
                Path.Combine(pluginDirectory, "homogenization.module.json"),
                """
                {
                  "Modules": {
                    "Homogenization": {
                      "Mes": {
                        "SignToken": "hdc2023"
                      }
                    }
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(tempDirectory);

            Assert.Equal("hdc2023", result.Configuration["Modules:Homogenization:Mes:SignToken"]);
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
