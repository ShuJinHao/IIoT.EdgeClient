using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;
using IIoT.Edge.Shell.Core;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void Load_WhenBindingV3WasMaterialized_ShouldUseAllSeventeenRoutesAsAuthoritativeRuntimeConfiguration()
    {
        const string clientCode = "CLIENT-P1";
        const string credentialReference = "IIoT.Edge/Pending/GEN-SHELL/CLIENT-P1";
        const string bootstrapSecret = "credential-manager-only-secret";
        var tempDirectory = CreateTempDirectory();
        var hostDirectory = Path.Combine(tempDirectory, "install", "current", "host");
        var dataRootOverride = Path.Combine(tempDirectory, "program-data");
        try
        {
            WriteText(
                Path.Combine(hostDirectory, "appsettings.json"),
                """
                {
                  "Shell": { "MachineProfile": "CLIENT-P1" },
                  "CloudApi": {
                    "BaseUrl": "https://packaged-default.invalid",
                    "Paths": {
                      "DeviceInstance": "/api/v1/edge/packaged-default"
                    }
                  }
                }
                """);
            var payload = CreateCanonicalV3Payload(credentialReference);
            var binding = Assert.Single(payload.Bindings);
            var materialized = new JsonObject();
            EdgeBindingMaterializer.MaterializeV3(
                materialized,
                payload,
                binding,
                $"plugins/{clientCode}",
                binding.PluginDirectory);

            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                WriteText(
                    EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(hostDirectory),
                    EdgeInstallerBindingCodec.SerializeRuntime(
                        EdgeInstallerBindingCodec.ToRuntime(payload, "S-1-5-21-1000")));
                var machineConfigPath = EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                    clientCode,
                    hostDirectory);
                WriteText(
                    machineConfigPath,
                    materialized.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                var packagedSettings = JsonNode.Parse(File.ReadAllText(
                    Path.Combine(hostDirectory, "appsettings.json")))!.AsObject();
                packagedSettings["Shell"]!["MachineConfigPath"] = machineConfigPath;
                WriteText(
                    Path.Combine(hostDirectory, "appsettings.json"),
                    packagedSettings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                var result = new ShellConfigurationLoader(
                        credentialStore: new FixedCredentialStore(
                            credentialReference,
                            bootstrapSecret))
                    .Load(hostDirectory);

                Assert.Equal(payload.BaseUrl, result.Configuration["CloudApi:BaseUrl"]);
                Assert.Equal(clientCode, result.Configuration["CloudApi:ClientCode"]);
                Assert.Equal(bootstrapSecret, result.Configuration["CloudApi:BootstrapSecret"]);
                Assert.Equal(EdgeBindingRouteCatalog.ExpectedRouteCount, EdgeBindingRouteCatalog.All.Count);
                Assert.All(EdgeBindingRouteCatalog.All, descriptor =>
                    Assert.Equal(
                        EdgeBindingRouteCatalog.Get(payload.Paths, descriptor.Key),
                        result.Configuration[$"CloudApi:Paths:{descriptor.MachineConfigKey}"]));
                Assert.DoesNotContain(bootstrapSecret, File.ReadAllText(machineConfigPath), StringComparison.Ordinal);
                Assert.DoesNotContain(bootstrapSecret, File.ReadAllText(
                    EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(hostDirectory)), StringComparison.Ordinal);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenLegacyPluginRuntimeConfigExists_ShouldNeverOverrideAppSettings()
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
    public void Load_WhenLegacyPluginRuntimeConfigIsAtPublishedModuleRoot_ShouldIgnoreIt()
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
                    "TestPlugin": {
                      "ModuleSeed": {
                        "Version": 1,
                        "Environment": "Production"
                      }
                    }
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

            Assert.Null(result.Configuration["Modules:TestPlugin:Module:Runtime:EventLoopIntervalMs"]);
            Assert.Equal(
                "False",
                result.Configuration["Modules:TestPlugin:Capabilities:RequiresProductionPlan"]);
            Assert.Contains(result.Issues, issue => issue.Code == "PLUGIN_RUNTIME_CONFIG_IGNORED");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenModuleSeedSelectionDoesNotMatchManifest_ShouldKeepShellConfigurationAndReportDiagnostic()
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
                    "TestPlugin": {
                      "ModuleSeed": {
                        "Version": 2,
                        "Environment": "Staging"
                      }
                    }
                  }
                }
                """);
            WriteValidPluginEnvelope(
                Path.Combine(tempDirectory, "plugins", "TestPlugin"),
                "TestPlugin");

            var result = new ShellConfigurationLoader().Load(hostDirectory);

            Assert.NotNull(result.Configuration);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "PLUGIN_MODULE_SEED_SELECTION_INVALID"
                         && issue.ModuleId == "TestPlugin");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenMultiplePluginRootsContainLegacyRuntimeConfig_ShouldIgnoreEveryCopy()
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
                    "PluginRoots": [ "../plugins-a", "../plugins-b" ],
                    "TestPlugin": {
                      "ModuleSeed": {
                        "Version": 1,
                        "Environment": "Production"
                      }
                    }
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

            Assert.Null(result.Configuration["Modules:TestPlugin:Module:Runtime:EventLoopIntervalMs"]);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "PLUGIN_MODULE_CONFIGURATION_OWNER_DUPLICATE");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Load_WhenVelopackCurrentHostContainsLegacyRuntimeConfig_ShouldIgnoreIt()
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
                    "PluginRoots": [ "../plugins" ],
                    "TestPlugin": {
                      "ModuleSeed": {
                        "Version": 1,
                        "Environment": "Production"
                      }
                    }
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

            Assert.Null(result.Configuration["Modules:TestPlugin:Module:Runtime:EventLoopIntervalMs"]);
            Assert.Contains(result.Issues, issue => issue.Code == "PLUGIN_RUNTIME_CONFIG_IGNORED");
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
    public void Load_WhenLegacyPluginRuntimeConfigContainsHostKeys_ShouldIgnoreEveryKey()
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
                    "Enabled": [ "TestPlugin" ],
                    "TestPlugin": {
                      "ModuleSeed": {
                        "Version": 1,
                        "Environment": "Production"
                      }
                    }
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
            Assert.Null(result.Configuration["Modules:TestPlugin:Module:Runtime:EventLoopIntervalMs"]);
            Assert.Contains(result.Issues, issue => issue.Code == "PLUGIN_RUNTIME_CONFIG_IGNORED");
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
                    "Enabled": [ "EnabledPlugin", "InvalidPlugin" ],
                    "EnabledPlugin": {
                      "ModuleSeed": {
                        "Version": 1,
                        "Environment": "Production"
                      }
                    },
                    "InvalidPlugin": {
                      "ModuleSeed": {
                        "Version": 1,
                        "Environment": "Production"
                      }
                    }
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

            Assert.Null(result.Configuration["Modules:EnabledPlugin:Value"]);
            Assert.Null(result.Configuration["Modules:DisabledPlugin:Value"]);
            Assert.Null(result.Configuration["Modules:InvalidPlugin:Value"]);
            Assert.Contains(result.Issues, issue => issue.Code == "PLUGIN_MANIFEST_INVALID");
            Assert.Contains(result.Issues, issue => issue.Code == "PLUGIN_RUNTIME_CONFIG_IGNORED");
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

    [Fact]
    public void Load_WhenPluginDiscoveryThrowsUnknownException_ShouldPropagateSameInstanceExactlyOnce()
    {
        var expected = new InvalidOperationException("unexpected catalog failure");

        AssertPluginDiscoveryExceptionPropagates(expected);
    }

    [Fact]
    public void Load_WhenPluginDiscoveryThrowsOperationCanceledException_ShouldPropagateSameInstanceExactlyOnce()
    {
        var expected = new OperationCanceledException("catalog canceled");

        AssertPluginDiscoveryExceptionPropagates(expected);
    }

    private static void AssertPluginDiscoveryExceptionPropagates(Exception expected)
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDirectory, "plugins"));
            WriteText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Modules": {
                    "Enabled": [ "TestPlugin" ],
                    "PluginRoots": [ "plugins" ]
                  }
                }
                """);
            var catalog = new ThrowingModuleCatalog(expected);

            var actual = Assert.Throws(expected.GetType(), () =>
                new ShellConfigurationLoader(catalog).Load(tempDirectory));

            Assert.Same(expected, actual);
            Assert.Equal(1, catalog.DiscoverCallCount);
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
        var schemaFileName = $"{moduleId.ToLowerInvariant()}.module.schema.json";
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
              "configurationSchema": "Config/{{schemaFileName}}",
              "moduleSeed": {
                "schemaVersion": 1,
                "currentVersion": 1,
                "supportedEnvironments": [ "Production" ],
                "newDevicesEnabled": false,
                "missingTaskBindingsEnabled": true,
                "resetBeforeImport": false
              },
              "capabilities": {
                "requiresProductionPlan": false
              },
              "dependencies": []
            }
            """);
        WriteText(
            Path.Combine(pluginDirectory, "Config", schemaFileName),
            $$"""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "x-moduleId": "{{moduleId}}",
              "required": [ "ModuleSeed" ],
              "properties": {
                "ModuleSeed": {
                  "type": "object",
                  "properties": {
                    "Version": { "const": 1 },
                    "Environment": { "const": "Production" }
                  }
                },
                "DeviceSeed": {
                  "type": "object",
                  "deprecated": true
                }
              }
            }
            """);
    }

    private static EdgeInstallerBindingEnvelope CreateCanonicalV3Payload(string credentialReference)
    {
        var now = DateTimeOffset.UtcNow;
        var paths = new EdgeInstallerBindingPaths(
            "/api/v1/edge/bootstrap/device-instance",
            "/api/v1/edge/bootstrap/refresh",
            "/api/v1/edge/devices/activate",
            "/api/v1/edge/devices/activate/confirm",
            "/api/v1/edge/identity/device-login",
            "/api/v1/edge/identity/human/refresh",
            "/api/v1/edge/identity/human/session-validation",
            "/api/v1/edge/device-logs",
            "/api/v1/edge/pass-stations/{typeKey}/batch",
            "/api/v1/edge/capacity/hourly",
            "/api/v1/edge/capacity/summary",
            "/api/v1/edge/capacity/summary/range",
            "/api/v1/edge/recipes/{deviceId}",
            "/api/v1/edge/client-releases/{deviceId}",
            "/api/v1/edge/client-versions",
            "/api/v1/edge/runtime-heartbeats",
            "/api/v1/edge/edge-hosts/plc-runtime-states");
        return new EdgeInstallerBindingEnvelope(
            EdgeInstallerBindingCodec.CurrentSchemaVersion,
            "GEN-SHELL",
            now,
            now.AddMinutes(30),
            "https://cloud.example.test",
            paths,
            [
                new EdgeInstallerDeviceBinding(
                    "CLIENT-P1",
                    "P1",
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "DieCutting",
                    "P1",
                    "2.0.21",
                    new string('A', 64),
                    "plugins/CLIENT-P1/app",
                    "plugins/CLIENT-P1/config",
                    "plugins/CLIENT-P1/db",
                    "plugins/CLIENT-P1/data",
                    "plugins/CLIENT-P1/logs",
                    "plugins/CLIENT-P1/cache",
                    "plugins/CLIENT-P1/context",
                    "plugins/CLIENT-P1/buffers",
                    credentialReference,
                    "pending-secret-never-materialized")
            ]);
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

    private sealed class ThrowingModuleCatalog(Exception exception) : IModuleCatalog
    {
        public int DiscoverCallCount { get; private set; }

        public ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath)
        {
            DiscoverCallCount++;
            throw exception;
        }

        public ModuleCatalogActivationResult CreateEnabledModules(
            IConfiguration configuration,
            string sectionName,
            IReadOnlyList<ModulePluginDescriptor> discoveredModules)
            => throw new NotSupportedException();

        public bool IsDiscoveredModule(
            string moduleId,
            IReadOnlyList<ModulePluginDescriptor> discoveredModules)
            => throw new NotSupportedException();
    }

    private sealed class FixedCredentialStore(string reference, string secret) : IEdgeCredentialStore
    {
        public void Write(string candidateReference, string candidateSecret)
            => throw new NotSupportedException();

        public string Read(string candidateReference)
            => string.Equals(candidateReference, reference, StringComparison.Ordinal)
                ? secret
                : throw new InvalidDataException("Unexpected credential reference.");

        public void Delete(string candidateReference)
            => throw new NotSupportedException();
    }
}
