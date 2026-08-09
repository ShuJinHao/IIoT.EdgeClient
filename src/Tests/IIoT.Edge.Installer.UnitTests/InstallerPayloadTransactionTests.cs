using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Installer;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Installer.UnitTests;

public sealed class InstallerPayloadTransactionTests
{
    private const string ClientCode = "CLIENT-P1";

    [Fact]
    public void Apply_WhenReinstalling_PreservesPersistentPluginBytesAndReplacesOnlyAppAndMachineConfig()
    {
        using var fixture = InstallerTransactionFixture.Create(withExistingPlugin: true);
        var before = fixture.CapturePersistentFiles();

        using var transaction = InstallerPayloadTransaction.Prepare(
            fixture.PayloadRoot,
            fixture.InstallRoot,
            fixture.CredentialStore,
            new AcceptAllSignatureVerifier(),
            new FixedCredentialOwnerSidProvider());
        transaction.CaptureCoreState();
        transaction.Apply();
        transaction.Commit();

        Assert.Equal("new-plugin-binary", File.ReadAllText(fixture.PluginFile("app", "Plugin.P1.dll")));
        Assert.False(File.Exists(fixture.PluginFile("app", "old-plugin.dll")));
        Assert.Contains(
            "CLIENT-P1",
            File.ReadAllText(fixture.PluginFile("config", "appsettings.machine.CLIENT-P1.json")),
            StringComparison.Ordinal);
        AssertPersistentFilesEqual(before, fixture.CapturePersistentFiles());
        Assert.Equal("operator-plc-settings", File.ReadAllText(fixture.PluginFile("config", "plc-settings.json")));
    }

    [Fact]
    public void Rollback_WhenPostSwitchValidationFails_RestoresAppAndMachineConfigWithoutTouchingPersistentBytes()
    {
        using var fixture = InstallerTransactionFixture.Create(withExistingPlugin: true);
        var before = fixture.CapturePersistentFiles();
        var oldMachineConfig = File.ReadAllBytes(
            fixture.PluginFile("config", "appsettings.machine.CLIENT-P1.json"));

        using var transaction = InstallerPayloadTransaction.Prepare(
            fixture.PayloadRoot,
            fixture.InstallRoot,
            fixture.CredentialStore,
            new AcceptAllSignatureVerifier(),
            new FixedCredentialOwnerSidProvider());
        transaction.CaptureCoreState();
        transaction.Apply();
        Assert.Equal("new-plugin-binary", File.ReadAllText(fixture.PluginFile("app", "Plugin.P1.dll")));

        transaction.Rollback();

        Assert.Equal("old-plugin-binary", File.ReadAllText(fixture.PluginFile("app", "old-plugin.dll")));
        Assert.False(File.Exists(fixture.PluginFile("app", "Plugin.P1.dll")));
        Assert.Equal(
            oldMachineConfig,
            File.ReadAllBytes(fixture.PluginFile("config", "appsettings.machine.CLIENT-P1.json")));
        AssertPersistentFilesEqual(before, fixture.CapturePersistentFiles());
        Assert.Equal("old-core", File.ReadAllText(Path.Combine(fixture.InstallRoot, "current", "old-core.txt")));
        Assert.False(fixture.CredentialStore.Contains(fixture.PendingCredentialReference));
    }

    [Fact]
    public void Apply_WhenFirstInstalling_CreatesAllPersistentDirectoriesWithoutSeedingBusinessData()
    {
        using var fixture = InstallerTransactionFixture.Create(withExistingPlugin: false);

        using var transaction = InstallerPayloadTransaction.Prepare(
            fixture.PayloadRoot,
            fixture.InstallRoot,
            fixture.CredentialStore,
            new AcceptAllSignatureVerifier(),
            new FixedCredentialOwnerSidProvider());
        transaction.CaptureCoreState();
        transaction.Apply();
        transaction.Commit();

        foreach (var directory in new[] { "db", "logs", "cache", "context", "buffers", "data" })
        {
            var path = fixture.PluginFile(directory);
            Assert.True(Directory.Exists(path), $"Expected first install to create {path}.");
            Assert.Empty(Directory.EnumerateFileSystemEntries(path));
        }
    }

    [Fact]
    public void Prepare_WhenEnabledPluginPackageHashDiffers_ShouldFailBeforeCredentialOrInstallWrite()
    {
        using var fixture = InstallerTransactionFixture.Create(withExistingPlugin: false);
        var selectionPath = Path.Combine(
            fixture.PayloadRoot,
            "launcher",
            "iiot-enabled-plugins.json");
        var selection = JsonNode.Parse(File.ReadAllText(selectionPath))!.AsObject();
        selection["plugins"]![0]!["packageSha256"] = new string('b', 64);
        File.WriteAllText(selectionPath, selection.ToJsonString());
        fixture.WritePayloadManifest();

        Assert.Throws<InvalidDataException>(() => InstallerPayloadTransaction.Prepare(
            fixture.PayloadRoot,
            fixture.InstallRoot,
            fixture.CredentialStore,
            new AcceptAllSignatureVerifier(),
            new FixedCredentialOwnerSidProvider()));

        Assert.False(fixture.CredentialStore.Contains(fixture.PendingCredentialReference));
        Assert.Equal(
            "old-core",
            File.ReadAllText(Path.Combine(fixture.InstallRoot, "current", "old-core.txt")));
    }

    private static void AssertPersistentFilesEqual(
        IReadOnlyDictionary<string, byte[]> expected,
        IReadOnlyDictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, actual[pair.Key]);
        }
    }

    private sealed class InstallerTransactionFixture : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private InstallerTransactionFixture(string root)
        {
            Root = root;
            PayloadRoot = Path.Combine(root, "payload");
            InstallRoot = Path.Combine(root, "install");
            PendingCredentialReference = WindowsCredentialManagerStore.CreatePendingReference(
                "generation-installer-transaction",
                ClientCode);
        }

        public string Root { get; }
        public string PayloadRoot { get; }
        public string InstallRoot { get; }
        public string PendingCredentialReference { get; }
        public InMemoryCredentialStore CredentialStore { get; } = new();

        public static InstallerTransactionFixture Create(bool withExistingPlugin)
        {
            var root = Path.Combine(Path.GetTempPath(), $"iiot-installer-transaction-{Guid.NewGuid():N}");
            var fixture = new InstallerTransactionFixture(root);
            Directory.CreateDirectory(fixture.PayloadRoot);
            Directory.CreateDirectory(Path.Combine(fixture.InstallRoot, "current"));
            File.WriteAllText(Path.Combine(fixture.InstallRoot, "current", "old-core.txt"), "old-core");
            fixture.WritePayload();
            if (withExistingPlugin)
            {
                fixture.WriteExistingPlugin();
            }

            return fixture;
        }

        public string PluginFile(params string[] segments)
            => Path.Combine([InstallRoot, "plugins", ClientCode, .. segments]);

        public IReadOnlyDictionary<string, byte[]> CapturePersistentFiles()
        {
            var pluginRoot = PluginFile();
            return new[] { "db", "logs", "cache", "context", "buffers", "data" }
                .SelectMany(directory => Directory.Exists(Path.Combine(pluginRoot, directory))
                    ? Directory.EnumerateFiles(Path.Combine(pluginRoot, directory), "*", SearchOption.AllDirectories)
                    : Enumerable.Empty<string>())
                .ToDictionary(
                    path => Path.GetRelativePath(pluginRoot, path).Replace('\\', '/'),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private void WriteExistingPlugin()
        {
            WriteText(PluginFile("app", "old-plugin.dll"), "old-plugin-binary");
            WriteText(
                PluginFile("config", "appsettings.machine.CLIENT-P1.json"),
                "{\"legacy\":true}");
            WriteText(PluginFile("config", "plc-settings.json"), "operator-plc-settings");
            WriteText(PluginFile("db", "plugin.db"), "sqlite-production-bytes");
            WriteText(PluginFile("logs", "runtime.log"), "operator-runtime-log");
            WriteText(PluginFile("cache", "plc-snapshot.bin"), "cached-plc-state");
            WriteText(PluginFile("context", "checkpoint.json"), "runtime-checkpoint");
            WriteText(PluginFile("buffers", "pipeline_cloud.db"), "cloud-retry-bytes");
            WriteText(PluginFile("buffers", "pipeline_mes.db"), "mes-retry-bytes");
            WriteText(PluginFile("buffers", "deadletter.db"), "deadletter-bytes");
            WriteText(PluginFile("data", "handoff.db"), "short-handoff-bytes");
        }

        private void WritePayload()
        {
            WriteText(Path.Combine(PayloadRoot, "velopack", "EdgeSetup.exe"), "velopack-setup");

            var launcherRoot = Path.Combine(PayloadRoot, "launcher");
            WriteRuntimeComponent(launcherRoot, "IIoT.Edge.Launcher");
            WriteJson(Path.Combine(launcherRoot, "iiot-enabled-plugins.json"), new
            {
                schemaVersion = 2,
                generatedAtUtc = DateTimeOffset.UtcNow,
                plugins = new[]
                {
                    new
                    {
                        moduleId = "Plugin.P1",
                        displayName = "P1",
                        version = "3.0.0",
                        packageSha256 = new string('a', 64),
                        pluginDirectory = ClientCode,
                        clientCode = ClientCode,
                        deviceName = "P1 正极模切",
                        processId = Guid.Parse("11111111-1111-1111-1111-111111111111")
                    }
                }
            });
            WriteText(Path.Combine(launcherRoot, "launcher.update.json"), "{}");
            WriteBinding(Path.Combine(launcherRoot, "iiot-binding.json"));

            var hostRoot = Path.Combine(PayloadRoot, "host");
            WriteRuntimeComponent(hostRoot, "IIoT.Edge.Shell");
            var hostAssembly = Path.Combine(hostRoot, "IIoT.Edge.Shell.dll");
            WriteText(hostAssembly, "host-shell-binary");
            var hostManifestPath = Path.Combine(PayloadRoot, "host-file-manifest.json");
            WriteJson(hostManifestPath, new
            {
                schemaVersion = 1,
                component = "EdgeHost",
                version = "2.0.12",
                files = new[]
                {
                    FileFact(hostRoot, hostAssembly, "managed", "EdgeHost", "2.0.12")
                }
            });

            var appRoot = Path.Combine(PayloadRoot, "plugins", ClientCode, "app");
            var entryAssembly = Path.Combine(appRoot, "Plugin.P1.dll");
            WriteText(entryAssembly, "new-plugin-binary");
            WriteJson(Path.Combine(appRoot, "plugin.json"), new
            {
                moduleId = "Plugin.P1",
                version = "3.0.0",
                entryAssembly = "Plugin.P1.dll",
                entryType = "Plugin.P1.Entry",
                supportedProcessType = "DieCutting"
            });
            WriteText(Path.Combine(appRoot, "appsettings.machine.Template.json"), "{}");
            WriteJson(Path.Combine(appRoot, "dependency-closure.json"), new
            {
                schemaVersion = 2,
                entryAssembly = "Plugin.P1.dll",
                plugin = new
                {
                    moduleId = "Plugin.P1",
                    version = "3.0.0",
                    targetRuntime = "win-x64"
                },
                host = new
                {
                    component = "EdgeHost",
                    version = "2.0.12",
                    fileManifestSha256 = Sha256(hostManifestPath)
                },
                dependencies = new[]
                {
                    new
                    {
                        library = "Plugin.P1",
                        libraryVersion = "3.0.0",
                        asset = "Plugin.P1.dll",
                        kind = "runtime",
                        source = "plugin",
                        publishPath = "Plugin.P1.dll",
                        owner = "Plugin.P1",
                        size = new FileInfo(entryAssembly).Length,
                        sha256 = Sha256(entryAssembly),
                        version = "3.0.0"
                    }
                }
            });
            WritePluginFileManifest(appRoot);
            WritePayloadManifest();
        }

        private void WriteBinding(string path)
        {
            var generatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            WriteJson(path, new
            {
                schemaVersion = 3,
                generationId = "generation-installer-transaction",
                generatedAtUtc = generatedAt,
                expiresAtUtc = generatedAt.AddDays(7),
                baseUrl = "https://cloud.test",
                paths = new
                {
                    deviceInstance = "/api/v1/edge/bootstrap/device-instance",
                    bootstrapRefresh = "/api/v1/edge/bootstrap/edge-refresh",
                    activateDevice = "/api/v1/edge/bootstrap/device-activate",
                    activateDeviceConfirm = "/api/v1/edge/bootstrap/device-activation-confirm",
                    identityDeviceLogin = "/api/v1/human/identity/edge-login",
                    humanIdentityRefresh = "/api/v1/human/identity/refresh",
                    humanSessionValidation = "/api/v1/human/identity/session",
                    deviceLog = "/api/v1/edge/device-logs",
                    passStationBatchTemplate = "/api/v1/edge/pass-stations/{typeKey}/batch",
                    capacityHourly = "/api/v1/edge/capacity/hourly",
                    capacitySummary = "/api/v1/edge/capacity/summary",
                    capacitySummaryRange = "/api/v1/edge/capacity/summary/range",
                    recipeByDeviceTemplate = "/api/v1/edge/recipes/device/{deviceId}",
                    clientReleaseCatalogTemplate = "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                    clientVersionReport = "/api/v1/edge/client-releases/version-reports",
                    runtimeHeartbeat = "/api/v1/edge/runtime-heartbeats",
                    edgeHostPlcRuntimeStates = "/api/v1/edge/edge-hosts/plc-runtime-states"
                },
                bindings = new[]
                {
                    new
                    {
                        clientCode = ClientCode,
                        deviceName = "P1 正极模切",
                        processId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        processType = "DieCutting",
                        moduleId = "Plugin.P1",
                        pluginVersion = "3.0.0",
                        packageSha256 = new string('a', 64),
                        pluginDirectory = $"plugins/{ClientCode}/app",
                        configDirectory = $"plugins/{ClientCode}/config",
                        dbDirectory = $"plugins/{ClientCode}/db",
                        dataDirectory = $"plugins/{ClientCode}/data",
                        logsDirectory = $"plugins/{ClientCode}/logs",
                        cacheDirectory = $"plugins/{ClientCode}/cache",
                        contextDirectory = $"plugins/{ClientCode}/context",
                        buffersDirectory = $"plugins/{ClientCode}/buffers",
                        pendingCredential = new
                        {
                            name = PendingCredentialReference,
                            secret = "short-lived-pending-secret"
                        }
                    }
                }
            });
        }

        public void WritePayloadManifest()
        {
            var files = Directory.EnumerateFiles(PayloadRoot, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    SelfExtractor.PayloadManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(path => FileFact(PayloadRoot, path, "payload", "Installer", "2.0.12"))
                .OrderBy(static fact => fact.Path, StringComparer.Ordinal)
                .ToArray();
            WriteJson(Path.Combine(PayloadRoot, SelfExtractor.PayloadManifestFileName), new
            {
                schemaVersion = 1,
                generationId = "generation-installer-transaction",
                component = "Installer",
                version = "2.0.12",
                createdAtUtc = DateTimeOffset.UtcNow,
                files,
                signature = new
                {
                    algorithm = "test-only",
                    keyId = "test-key",
                    value = "accepted-by-test-verifier"
                }
            });
        }

        private static void WritePluginFileManifest(string appRoot)
        {
            var files = Directory.EnumerateFiles(appRoot, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    "file-manifest.json",
                    StringComparison.OrdinalIgnoreCase))
                .Select(path => new
                {
                    path = Path.GetRelativePath(appRoot, path).Replace('\\', '/'),
                    size = new FileInfo(path).Length,
                    sha256 = Sha256(path)
                })
                .OrderBy(static fact => fact.path, StringComparer.Ordinal)
                .ToArray();
            WriteJson(Path.Combine(appRoot, "file-manifest.json"), new
            {
                schemaVersion = 1,
                component = "Plugin.P1",
                version = "3.0.0",
                files
            });
        }

        private static void WriteRuntimeComponent(string root, string component)
        {
            WriteText(Path.Combine(root, $"{component}.exe"), $"{component}-exe");
            WriteText(Path.Combine(root, $"{component}.deps.json"), "{\"targets\":{}}");
            WriteText(Path.Combine(root, $"{component}.runtimeconfig.json"), "{}");
            foreach (var runtimeFile in new[]
                     {
                         "coreclr.dll",
                         "hostfxr.dll",
                         "hostpolicy.dll",
                         "System.Private.CoreLib.dll"
                     })
            {
                WriteText(Path.Combine(root, runtimeFile), $"{component}-{runtimeFile}");
            }
        }

        private static TestFileFact FileFact(
            string root,
            string path,
            string type,
            string component,
            string version)
            => new(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                new FileInfo(path).Length,
                Sha256(path),
                type,
                component,
                version);

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static void WriteJson(string path, object value)
            => WriteText(path, JsonSerializer.Serialize(value, JsonOptions));

        private static void WriteText(string path, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }

        private sealed record TestFileFact(
            string Path,
            long Size,
            string Sha256,
            string Type,
            string Component,
            string Version);
    }

    private sealed class AcceptAllSignatureVerifier : IInstallerPayloadSignatureVerifier
    {
        public void Verify(InstallerPayloadManifest manifest, ReadOnlySpan<byte> canonicalManifest)
        {
        }
    }

    private sealed class FixedCredentialOwnerSidProvider : IEdgeCredentialOwnerSidProvider
    {
        public string GetCurrentOwnerSid() => "S-1-5-21-1000";
    }

    private sealed class InMemoryCredentialStore : IEdgeCredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public bool Contains(string reference) => _values.ContainsKey(reference);

        public void Write(string reference, string secret) => _values[reference] = secret;

        public string Read(string reference)
            => _values.TryGetValue(reference, out var value)
                ? value
                : throw new KeyNotFoundException(reference);

        public void Delete(string reference) => _values.Remove(reference);
    }
}
