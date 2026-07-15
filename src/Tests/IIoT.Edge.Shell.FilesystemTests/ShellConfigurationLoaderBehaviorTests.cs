using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.Shell.Core;
using System.Text;
using Xunit;

namespace IIoT.Edge.Shell.FilesystemTests;

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
                    "MachineProfile": "TestMachineProfile"
                  },
                  "Modules": {
                    "Enabled": [ "TestPlugin" ]
                  }
                }
                """);
            WriteText(
                Path.Combine(tempDirectory, "appsettings.machine.TestMachineProfile.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "TestPlugin" ]
                  }
                }
                """);

            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                var result = new ShellConfigurationLoader().Load(tempDirectory);

                Assert.Equal("TestMachineProfile", result.MachineProfile);
                Assert.True(result.IsMachineProfileLoaded);
                Assert.True(result.IsExternalMachineProfileLoaded);
                Assert.Equal("TestPlugin", result.Configuration["Modules:Enabled:0"]);
                Assert.Equal("True", result.Configuration["Shell:MachineProfileLoaded"]);
                Assert.True(File.Exists(EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("TestMachineProfile")));
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
                    "Enabled": [ "TestPlugin" ]
                  }
                }
                """);

            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                var result = new ShellConfigurationLoader().Load(tempDirectory);

                Assert.Equal("MissingLine", result.MachineProfile);
                Assert.False(result.IsMachineProfileLoaded);
                Assert.False(result.IsExternalMachineProfileLoaded);
                Assert.Equal("TestPlugin", result.Configuration["Modules:Enabled:0"]);
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
                    "MachineProfile": "TestMachineProfile"
                  }
                }
                """);
            WriteText(
                Path.Combine(tempDirectory, "appsettings.machine.TestMachineProfile.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "TestPlugin" ]
                  }
                }
                """);

            Environment.SetEnvironmentVariable(environmentVariable, "TestMachineProfile");

            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                var result = new ShellConfigurationLoader().Load(tempDirectory);

                Assert.Equal("TestMachineProfile", result.MachineProfile);
                Assert.True(result.IsMachineProfileLoaded);
                Assert.True(result.IsExternalMachineProfileLoaded);
                Assert.Equal("TestPlugin", result.Configuration["Modules:Enabled:0"]);
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
                    "MachineProfile": "TestMachineProfile"
                  }
                }
                """);
            WriteText(
                Path.Combine(tempDirectory, "appsettings.machine.TestMachineProfile.json"),
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
                var externalPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("TestMachineProfile");
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
                    "TestPlugin": {
                      "Module": {
                        "Presentation": {
                          "MaxOutboundRecords": 25
                        }
                      }
                    }
                  }
                }
                """);
            var pluginConfigDirectory = Path.Combine(tempDirectory, "plugins", "TestPlugin", "Config");
            Directory.CreateDirectory(pluginConfigDirectory);
            WriteText(
                Path.Combine(pluginConfigDirectory, "test-plugin.module.json"),
                """
                {
                  "Modules": {
                    "TestPlugin": {
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

            Assert.Equal("25", result.Configuration["Modules:TestPlugin:Module:Presentation:MaxOutboundRecords"]);
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
                    "Enabled": [ "TestPlugin" ]
                  }
                }
                """);
            var pluginDirectory = Path.Combine(tempDirectory, "plugins", "TestPlugin");
            Directory.CreateDirectory(pluginDirectory);
            WriteValidPluginEnvelope(pluginDirectory, "TestPlugin");
            WriteText(
                Path.Combine(pluginDirectory, "test-plugin.module.json"),
                """
                {
                  "Modules": {
                    "TestPlugin": {
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

            Assert.Equal("50", result.Configuration["Modules:TestPlugin:Module:Runtime:EventLoopIntervalMs"]);
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
                    "Enabled": [ "TestPlugin" ],
                    "PluginRoots": [ "../plugins-a", "../plugins-b" ]
                  }
                }
                """);
            WriteValidPluginEnvelope(Path.Combine(tempDirectory, "plugins-a", "TestPlugin"), "TestPlugin");
            WriteText(
                Path.Combine(tempDirectory, "plugins-a", "TestPlugin", "Config", "test-plugin.module.json"),
                """
                {
                  "Modules": {
                    "TestPlugin": {
                      "Module": {
                        "Runtime": {
                          "EventLoopIntervalMs": 50
                        }
                      }
                    }
                  }
                }
                """);
            WriteValidPluginEnvelope(Path.Combine(tempDirectory, "plugins-b", "TestPlugin"), "TestPlugin");

            WriteText(
                Path.Combine(tempDirectory, "plugins-b", "TestPlugin", "Config", "test-plugin.module.json"),
                """
                {
                  "Modules": {
                    "TestPlugin": {
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

            Assert.Equal("75", result.Configuration["Modules:TestPlugin:Module:Runtime:EventLoopIntervalMs"]);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenVelopackCurrentHostUsesDefaultPluginRoot_ShouldLoadDefaultsFromInstallRootPlugins()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "install", "current", "host");
            WriteText(
                Path.Combine(hostDirectory, "appsettings.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "TestPlugin" ],
                    "PluginRoots": [ "../plugins" ]
                  }
                }
                """);
            WriteValidPluginEnvelope(
                Path.Combine(tempDirectory, "install", "plugins", "TestPlugin"),
                "TestPlugin");
            WriteText(
                Path.Combine(tempDirectory, "install", "plugins", "TestPlugin", "Config", "test-plugin.module.json"),
                """
                {
                  "Modules": {
                    "TestPlugin": {
                      "Module": {
                        "Runtime": {
                          "EventLoopIntervalMs": 125
                        }
                      }
                    }
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(hostDirectory);

            Assert.Equal("125", result.Configuration["Modules:TestPlugin:Module:Runtime:EventLoopIntervalMs"]);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenBaseJsonIsMalformed_ShouldReturnSafeConfigurationAndDiagnostic()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(Path.Combine(tempDirectory, "appsettings.json"), "{ not-json");

            var result = new ShellConfigurationLoader().Load(tempDirectory);

            Assert.NotNull(result.Configuration);
            Assert.Equal("Production", result.EnvironmentName);
            Assert.Contains(result.Issues, issue => issue.Code == "APPSETTINGS_BASE_UNAVAILABLE");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenMachineProfileContainsTraversal_ShouldIgnoreProfileAndExposeDiagnostic()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Shell": {
                    "MachineProfile": "../../escaped"
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(tempDirectory);

            Assert.Null(result.MachineProfile);
            Assert.False(result.IsMachineProfileLoaded);
            Assert.Null(result.Configuration["Shell:MachineProfilePath"]);
            Assert.Contains(result.Issues, issue => issue.Code == "MACHINE_PROFILE_NAME_INVALID");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenEnvironmentNameContainsTraversal_ShouldFallBackToProduction()
    {
        var tempDirectory = CreateTempDirectory();
        const string environmentVariable = "DOTNET_ENVIRONMENT";
        var originalValue = Environment.GetEnvironmentVariable(environmentVariable);
        try
        {
            WriteText(Path.Combine(tempDirectory, "appsettings.json"), "{}");
            Environment.SetEnvironmentVariable(environmentVariable, "../escaped");

            var result = new ShellConfigurationLoader().Load(tempDirectory);

            Assert.Equal("Production", result.EnvironmentName);
            Assert.Equal("Production", result.Configuration["Shell:Environment"]);
            Assert.Contains(result.Issues, issue => issue.Code == "SHELL_ENVIRONMENT_NAME_INVALID");
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, originalValue);
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenEnabledPluginDefaultContainsHostKeys_ShouldLoadOnlyOwnModuleSubtree()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            WriteText(
                Path.Combine(hostDirectory, "appsettings.json"),
                """
                {
                  "InstanceId": "SafeInstance",
                  "Modules": {
                    "Enabled": [ "TestPlugin" ]
                  }
                }
                """);
            var pluginDirectory = Path.Combine(tempDirectory, "plugins", "TestPlugin");
            WriteValidPluginEnvelope(pluginDirectory, "TestPlugin");
            WriteText(
                Path.Combine(pluginDirectory, "Config", "test-plugin.module.json"),
                """
                {
                  "InstanceId": "InjectedInstance",
                  "CloudApi": {
                    "ClientCode": "injected-client"
                  },
                  "Modules": {
                    "Enabled": [ "InjectedPlugin" ],
                    "OtherPlugin": {
                      "Injected": true
                    },
                    "TestPlugin": {
                      "Module": {
                        "Runtime": {
                          "EventLoopIntervalMs": 88
                        }
                      }
                    }
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(hostDirectory);

            Assert.Equal("SafeInstance", result.Configuration["InstanceId"]);
            Assert.Null(result.Configuration["CloudApi:ClientCode"]);
            Assert.Equal("TestPlugin", result.Configuration["Modules:Enabled:0"]);
            Assert.Null(result.Configuration["Modules:OtherPlugin:Injected"]);
            Assert.Equal("88", result.Configuration["Modules:TestPlugin:Module:Runtime:EventLoopIntervalMs"]);
            Assert.Contains(result.Issues, issue => issue.Code == "PLUGIN_DEFAULT_SCOPE_REJECTED");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenPluginIsDisabledOrManifestIsInvalid_ShouldNotLoadItsDefaults()
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
                    "Enabled": [ "EnabledPlugin", "InvalidPlugin" ]
                  }
                }
                """);
            var enabledDirectory = Path.Combine(tempDirectory, "plugins", "EnabledPlugin");
            WriteValidPluginEnvelope(enabledDirectory, "EnabledPlugin");
            WriteText(
                Path.Combine(enabledDirectory, "Config", "enabled.module.json"),
                """
                {
                  "Modules": {
                    "EnabledPlugin": { "Value": "enabled" }
                  }
                }
                """);
            var disabledDirectory = Path.Combine(tempDirectory, "plugins", "DisabledPlugin");
            WriteValidPluginEnvelope(disabledDirectory, "DisabledPlugin");
            WriteText(
                Path.Combine(disabledDirectory, "Config", "disabled.module.json"),
                """
                {
                  "Modules": {
                    "DisabledPlugin": { "Value": "must-not-load" }
                  }
                }
                """);
            var invalidDirectory = Path.Combine(tempDirectory, "plugins", "InvalidPlugin");
            WriteValidPluginEnvelope(invalidDirectory, "InvalidPlugin");
            File.Delete(Path.Combine(invalidDirectory, "InvalidPlugin.dll"));
            WriteText(
                Path.Combine(invalidDirectory, "Config", "invalid.module.json"),
                """
                {
                  "Modules": {
                    "InvalidPlugin": { "Value": "must-not-load" }
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(hostDirectory);

            Assert.Equal("enabled", result.Configuration["Modules:EnabledPlugin:Value"]);
            Assert.Null(result.Configuration["Modules:DisabledPlugin:Value"]);
            Assert.Null(result.Configuration["Modules:InvalidPlugin:Value"]);
            Assert.Contains(result.Issues, issue => issue.Code == "PLUGIN_MANIFEST_INVALID");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenConfiguredPluginRootIsInvalid_ShouldContinueWithDiagnostic()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            WriteText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "TestPlugin" ],
                    "PluginRoots": [ "bad\u0000root" ]
                  }
                }
                """);

            var result = new ShellConfigurationLoader().Load(tempDirectory);

            Assert.NotNull(result.Configuration);
            Assert.Contains(result.Issues, issue => issue.Code == "PLUGIN_ROOT_PATH_INVALID");
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

    private static void WriteValidPluginEnvelope(string pluginDirectory, string moduleId)
    {
        Directory.CreateDirectory(pluginDirectory);
        WriteText(Path.Combine(pluginDirectory, $"{moduleId}.dll"), "staged-test-assembly");
        WriteText(
            Path.Combine(pluginDirectory, "plugin.json"),
            $$"""
            {
              "moduleId": "{{moduleId}}",
              "displayName": "{{moduleId}}",
              "version": "1.0.0",
              "hostApiVersion": "1.0.0",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "99.0.0",
              "entryAssembly": "{{moduleId}}.dll",
              "entryType": "{{moduleId}}.DependencyInjection",
              "supportedProcessType": "{{moduleId}}",
              "dependencies": []
            }
            """);
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
