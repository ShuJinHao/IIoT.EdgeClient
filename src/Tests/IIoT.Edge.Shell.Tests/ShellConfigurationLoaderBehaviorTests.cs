using IIoT.Edge.SharedKernel.Configuration;
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
        var dataRootOverride = Path.Combine(tempDirectory, "data-root");
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

            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                var result = new ShellConfigurationLoader().Load(tempDirectory);

                Assert.Equal("HomogenizationLine", result.MachineProfile);
                Assert.True(result.IsMachineProfileLoaded);
                Assert.True(result.IsExternalMachineProfileLoaded);
                Assert.Equal("Homogenization", result.Configuration["Modules:Enabled:0"]);
                Assert.Equal("True", result.Configuration["Shell:MachineProfileLoaded"]);
                Assert.True(File.Exists(EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("HomogenizationLine")));
            });
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
        var dataRootOverride = Path.Combine(tempDirectory, "data-root");
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

            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                var result = new ShellConfigurationLoader().Load(tempDirectory);

                Assert.Equal("MissingLine", result.MachineProfile);
                Assert.False(result.IsMachineProfileLoaded);
                Assert.False(result.IsExternalMachineProfileLoaded);
                Assert.Equal("Homogenization", result.Configuration["Modules:Enabled:0"]);
                Assert.Equal("False", result.Configuration["Shell:MachineProfileLoaded"]);
                Assert.Equal("appsettings.machine.MissingLine.json", result.Configuration["Shell:MachineProfileFileName"]);
            });
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
        var dataRootOverride = Path.Combine(tempDirectory, "data-root");
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

            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                var result = new ShellConfigurationLoader().Load(tempDirectory);

                Assert.Equal("HomogenizationLine", result.MachineProfile);
                Assert.True(result.IsMachineProfileLoaded);
                Assert.True(result.IsExternalMachineProfileLoaded);
                Assert.Equal("Homogenization", result.Configuration["Modules:Enabled:0"]);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, originalValue);
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenExternalMachineProfileAlreadyExists_ShouldPreferItAndNeverOverwriteIt()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRootOverride = Path.Combine(tempDirectory, "data-root");
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
                  "Shell": {
                    "RuntimeDataRoot": "packaged-template"
                  },
                  "Modules": {
                    "Enabled": [ "Packaged" ]
                  }
                }
                """);

            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                var externalPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("HomogenizationLine");
                WriteText(
                    externalPath,
                    """
                    {
                      "Shell": {
                        "RuntimeDataRoot": "external-protected"
                      },
                      "Modules": {
                        "Enabled": [ "External" ]
                      },
                      "CloudApi": {
                        "ClientCode": "protected-client"
                      }
                    }
                    """);
                var originalExternalProfile = File.ReadAllText(externalPath);

                var result = new ShellConfigurationLoader().Load(tempDirectory);

                Assert.True(result.IsMachineProfileLoaded);
                Assert.True(result.IsExternalMachineProfileLoaded);
                Assert.Equal("external-protected", result.Configuration["Shell:RuntimeDataRoot"]);
                Assert.Equal("External", result.Configuration["Modules:Enabled:0"]);
                Assert.Equal("protected-client", result.Configuration["CloudApi:ClientCode"]);
                Assert.Equal(originalExternalProfile, File.ReadAllText(externalPath));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenPluginDefaultAndAppSettingsProvideSameModuleKey_ShouldPreferAppSettings()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            WriteText(
                Path.Combine(hostDirectory, "appsettings.json"),
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
            var pluginConfigDirectory = Path.Combine(tempDirectory, "plugins", "Homogenization", "Config");
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

            var result = new ShellConfigurationLoader().Load(hostDirectory);

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
            var hostDirectory = Path.Combine(tempDirectory, "host");
            WriteText(
                Path.Combine(hostDirectory, "appsettings.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "Homogenization" ]
                  }
                }
                """);
            var pluginDirectory = Path.Combine(tempDirectory, "plugins", "Homogenization");
            Directory.CreateDirectory(pluginDirectory);
            WriteText(
                Path.Combine(pluginDirectory, "homogenization.module.json"),
                """
                {
                  "Modules": {
                    "Homogenization": {
                      "Module": {
                        "Runtime": {
                          "EventLoopIntervalMs": 50
                        }
                      }
                    }
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(hostDirectory);

            Assert.Equal("50", result.Configuration["Modules:Homogenization:Module:Runtime:EventLoopIntervalMs"]);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenMultiplePluginRootsAreConfigured_ShouldApplyLaterDefaultsAfterEarlierDefaults()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            WriteText(
                Path.Combine(hostDirectory, "appsettings.json"),
                """
                {
                  "Modules": {
                    "PluginRoots": [ "../plugins-a", "../plugins-b" ]
                  }
                }
                """);
            WriteText(
                Path.Combine(tempDirectory, "plugins-a", "Homogenization", "Config", "homogenization.module.json"),
                """
                {
                  "Modules": {
                    "Homogenization": {
                      "Module": {
                        "Runtime": {
                          "EventLoopIntervalMs": 50
                        }
                      }
                    }
                  }
                }
                """);

            WriteText(
                Path.Combine(tempDirectory, "plugins-b", "Homogenization", "Config", "homogenization.module.json"),
                """
                {
                  "Modules": {
                    "Homogenization": {
                      "Module": {
                        "Runtime": {
                          "EventLoopIntervalMs": 75
                        }
                      }
                    }
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(hostDirectory);

            Assert.Equal("75", result.Configuration["Modules:Homogenization:Module:Runtime:EventLoopIntervalMs"]);
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
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

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
