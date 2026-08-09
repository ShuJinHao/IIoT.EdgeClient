using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;
using IIoT.Edge.UI.Shared.Localization;
using Xunit;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class LauncherStartupSecurityTests
{
    [Fact]
    public void MachineLock_WhenAnotherThreadOwnsIt_ShouldRejectSecondLauncherBeforeWork()
    {
        var mutexName = $"IIoT.Edge.Launcher.Tests.{Guid.NewGuid():N}";
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        LauncherMachineLock? first = null;
        var thread = new Thread(() =>
        {
            first = LauncherMachineLock.TryAcquire(mutexName);
            acquired.Set();
            release.Wait();
            first?.Dispose();
        });
        thread.Start();
        Assert.True(acquired.Wait(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken));
        Assert.NotNull(first);

        var second = LauncherMachineLock.TryAcquire(mutexName);

        Assert.Null(second);
        release.Set();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void StartupCoordinator_ShouldUseFailClosedRecoveryIdentityAndMaterializationOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"iiot-launcher-order-{Guid.NewGuid():N}");
        var calls = new List<string>();
        try
        {
            var gate = new FileLauncherUpdateOperationGate(root);
            var coordinator = new LauncherStartupCoordinator(
                new NoOpLanguageService(),
                new RecordingAccountInitializer(calls),
                new RecordingUpdateInitializer(calls),
                gate,
                new RecordingRecovery(calls),
                new RecordingActivation(calls),
                new RecordingImporter(calls),
                new LauncherStartupDiagnosticStore(),
                new RecordingRuntimePreflight(calls),
                new RecordingCredentialMigrator(calls));

            coordinator.Initialize();

            Assert.Equal(
                [
                    "update-recovery",
                    "identity-before-writes",
                    "host-db",
                    "legacy-activation",
                    "binding-v2-migration",
                    "credential-migration",
                    "binding-v3-complete",
                    "update-config"
                ],
                calls);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void RuntimePreflight_WhenCredentialOwnerDiffers_ShouldFailBeforeCredentialRead()
    {
        using var fixture = RuntimeFixture.Create();
        var store = new InMemoryCredentialStore();
        var preflight = fixture.CreatePreflight(
            store,
            new FixedSidProvider("S-1-5-21-2000"));

        var exception = Assert.Throws<InvalidDataException>(
            preflight.ValidateIdentityBeforeWrites);

        Assert.Contains("LAUNCHER_CREDENTIAL_OWNER_SID_MISMATCH", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.ReadCount);
    }

    [Fact]
    public void RuntimePreflight_ShouldValidateBindingCredentialMaterializationAndExactPluginBytes()
    {
        using var fixture = RuntimeFixture.Create();
        var store = new InMemoryCredentialStore();
        store.Write(fixture.CredentialReference, "pending-secret");
        var preflight = fixture.CreatePreflight(
            store,
            new FixedSidProvider(RuntimeFixture.OwnerSid));

        preflight.ValidateIdentityBeforeWrites();
        preflight.ValidateCompleteRuntime();

        Assert.True(store.ReadCount > 0);
    }

    [Fact]
    public void RuntimePreflight_WhenPluginBytesAreTampered_ShouldFailClosed()
    {
        using var fixture = RuntimeFixture.Create();
        File.AppendAllText(fixture.PluginManifestPath, " ");
        var store = new InMemoryCredentialStore();
        store.Write(fixture.CredentialReference, "pending-secret");
        var preflight = fixture.CreatePreflight(
            store,
            new FixedSidProvider(RuntimeFixture.OwnerSid));

        Assert.Throws<InvalidOperationException>(preflight.ValidateCompleteRuntime);
    }

    [Fact]
    public void LegacyCredentialMigrator_ShouldMoveBootstrapAndRefreshSecretsAtomically()
    {
        using var fixture = LegacyCredentialFixture.Create();
        var store = new InMemoryCredentialStore();
        var migrator = new LauncherLegacyCredentialMigrator(fixture.BaseDirectory, store);

        migrator.Migrate();

        var machine = File.ReadAllText(fixture.MachineConfigPath);
        var cache = File.ReadAllText(fixture.CachePath);
        Assert.DoesNotContain("bootstrap-plaintext", machine, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-plaintext", cache, StringComparison.Ordinal);
        Assert.DoesNotContain("access-plaintext", cache, StringComparison.Ordinal);
        Assert.Contains("BootstrapCredentialReference", machine, StringComparison.Ordinal);
        Assert.Contains("RefreshCredentialReference", cache, StringComparison.Ordinal);
        Assert.Equal(
            "bootstrap-plaintext",
            store.Read(WindowsCredentialManagerStore.CreateBootstrapReference(RuntimeFixture.ClientCode)));
        Assert.Equal(
            "refresh-plaintext",
            store.Read(WindowsCredentialManagerStore.CreateSessionReference(RuntimeFixture.ClientCode)));
    }

    [Fact]
    public void LegacyCredentialMigrator_WhenCredentialRoundTripFails_ShouldPreserveSources()
    {
        using var fixture = LegacyCredentialFixture.Create();
        var originalMachine = File.ReadAllBytes(fixture.MachineConfigPath);
        var originalCache = File.ReadAllBytes(fixture.CachePath);
        var migrator = new LauncherLegacyCredentialMigrator(
            fixture.BaseDirectory,
            new FailingRoundTripCredentialStore());

        Assert.Throws<InvalidDataException>(migrator.Migrate);

        Assert.Equal(originalMachine, File.ReadAllBytes(fixture.MachineConfigPath));
        Assert.Equal(originalCache, File.ReadAllBytes(fixture.CachePath));
    }

    private sealed class RuntimeFixture : IDisposable
    {
        public const string ClientCode = "CLIENT-P1";
        public const string OwnerSid = "S-1-5-21-1000";
        private const string ModuleId = "P1";
        private const string PluginVersion = "2.0.21";
        private const string PackageSha256 =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        private readonly string? _previousDataRoot;

        private RuntimeFixture(
            string root,
            string baseDirectory,
            string credentialReference,
            string pluginManifestPath,
            string? previousDataRoot)
        {
            Root = root;
            BaseDirectory = baseDirectory;
            CredentialReference = credentialReference;
            PluginManifestPath = pluginManifestPath;
            _previousDataRoot = previousDataRoot;
        }

        public string Root { get; }
        public string BaseDirectory { get; }
        public string CredentialReference { get; }
        public string PluginManifestPath { get; }

        public static RuntimeFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"iiot-launcher-v3-{Guid.NewGuid():N}");
            var baseDirectory = Path.Combine(root, "install", "current", "launcher");
            Directory.CreateDirectory(baseDirectory);
            var dataRoot = Path.Combine(root, "program-data");
            var previous = Environment.GetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                dataRoot);

            var payload = EdgeInstallerBindingCodec.ParsePayload(CreatePayloadJson());
            var runtime = EdgeInstallerBindingCodec.ToRuntime(payload, OwnerSid);
            var runtimePath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(baseDirectory);
            WriteText(runtimePath, EdgeInstallerBindingCodec.SerializeRuntime(runtime));

            var binding = Assert.Single(payload.Bindings);
            var machineRoot = new JsonObject();
            EdgeBindingMaterializer.MaterializeV3(
                machineRoot,
                payload,
                binding,
                $"plugins/{ClientCode}",
                binding.PluginDirectory);
            WriteText(
                EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                    ClientCode,
                    baseDirectory),
                machineRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var launcherConfigDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory);
            WriteText(
                Path.Combine(
                    launcherConfigDirectory,
                    LauncherEnabledPluginSelectionSource.EnabledPluginsFileName),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    plugins = new[]
                    {
                        new
                        {
                            moduleId = ModuleId,
                            displayName = "P1",
                            version = PluginVersion,
                            packageSha256 = PackageSha256,
                            pluginDirectory = ClientCode,
                            clientCode = ClientCode,
                            deviceName = "P1",
                            processId = "11111111-1111-1111-1111-111111111111"
                        }
                    }
                }));

            var pluginApp = Path.Combine(
                EdgeClientProgramDataPaths.ResolveDevicePluginRoot(ClientCode, baseDirectory),
                "app");
            Directory.CreateDirectory(pluginApp);
            var pluginManifestPath = Path.Combine(pluginApp, "plugin.json");
            WriteText(
                pluginManifestPath,
                JsonSerializer.Serialize(new
                {
                    moduleId = ModuleId,
                    version = PluginVersion,
                    supportedProcessType = "DieCutting",
                    entryAssembly = "P1.dll"
                }));
            var pluginBytes = File.ReadAllBytes(pluginManifestPath);
            WriteText(
                Path.Combine(pluginApp, "file-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    component = ModuleId,
                    version = PluginVersion,
                    files = new[]
                    {
                        new
                        {
                            path = "plugin.json",
                            size = pluginBytes.LongLength,
                            sha256 = Convert.ToHexString(SHA256.HashData(pluginBytes))
                        }
                    }
                }));

            return new RuntimeFixture(
                root,
                baseDirectory,
                binding.PendingCredentialReference,
                pluginManifestPath,
                previous);
        }

        public LauncherRuntimePreflight CreatePreflight(
            IEdgeCredentialStore store,
            IEdgeCredentialOwnerSidProvider sidProvider)
        {
            var selection = new LauncherEnabledPluginSelectionSource(BaseDirectory);
            var profileCatalog = new LauncherProfileCatalog(BaseDirectory);
            return new LauncherRuntimePreflight(
                BaseDirectory,
                profileCatalog,
                store,
                sidProvider);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                _previousDataRoot);
            DeleteDirectory(Root);
        }

        private static string CreatePayloadJson()
        {
            var now = DateTimeOffset.UtcNow;
            var root = new JsonObject
            {
                ["schemaVersion"] = 3,
                ["generationId"] = "GEN-LAUNCHER-V3",
                ["generatedAtUtc"] = now.ToString("O"),
                ["expiresAtUtc"] = now.AddMinutes(30).ToString("O"),
                ["baseUrl"] = "https://cloud.example.test",
                ["paths"] = CanonicalPaths(),
                ["bindings"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["clientCode"] = ClientCode,
                        ["deviceName"] = "P1",
                        ["processId"] = "11111111-1111-1111-1111-111111111111",
                        ["processType"] = "DieCutting",
                        ["moduleId"] = ModuleId,
                        ["pluginVersion"] = PluginVersion,
                        ["packageSha256"] = PackageSha256,
                        ["pluginDirectory"] = $"plugins/{ClientCode}/app",
                        ["configDirectory"] = $"plugins/{ClientCode}/config",
                        ["dbDirectory"] = $"plugins/{ClientCode}/db",
                        ["dataDirectory"] = $"plugins/{ClientCode}/data",
                        ["logsDirectory"] = $"plugins/{ClientCode}/logs",
                        ["cacheDirectory"] = $"plugins/{ClientCode}/cache",
                        ["contextDirectory"] = $"plugins/{ClientCode}/context",
                        ["buffersDirectory"] = $"plugins/{ClientCode}/buffers",
                        ["pendingCredential"] = new JsonObject
                        {
                            ["name"] = WindowsCredentialManagerStore.CreatePendingReference(
                                "GEN-LAUNCHER-V3",
                                ClientCode),
                            ["secret"] = "pending-secret"
                        }
                    }
                }
            };
            return root.ToJsonString();
        }

        private static JsonObject CanonicalPaths() => new()
        {
            ["deviceInstance"] = "/api/v1/edge/bootstrap/device-instance",
            ["bootstrapRefresh"] = "/api/v1/edge/bootstrap/edge-refresh",
            ["activateDevice"] = "/api/v1/edge/bootstrap/device-activate",
            ["activateDeviceConfirm"] = "/api/v1/edge/bootstrap/device-activation-confirm",
            ["identityDeviceLogin"] = "/api/v1/human/identity/edge-login",
            ["humanIdentityRefresh"] = "/api/v1/human/identity/refresh",
            ["humanSessionValidation"] = "/api/v1/human/identity/session",
            ["deviceLog"] = "/api/v1/edge/device-logs",
            ["passStationBatchTemplate"] = "/api/v1/edge/pass-stations/{typeKey}/batch",
            ["capacityHourly"] = "/api/v1/edge/capacity/hourly",
            ["capacitySummary"] = "/api/v1/edge/capacity/summary",
            ["capacitySummaryRange"] = "/api/v1/edge/capacity/summary/range",
            ["recipeByDeviceTemplate"] = "/api/v1/edge/recipes/device/{deviceId}",
            ["clientReleaseCatalogTemplate"] = "/api/v1/edge/client-releases/device/{deviceId}/catalog",
            ["clientVersionReport"] = "/api/v1/edge/client-releases/version-reports",
            ["runtimeHeartbeat"] = "/api/v1/edge/runtime-heartbeats",
            ["edgeHostPlcRuntimeStates"] = "/api/v1/edge/edge-hosts/plc-runtime-states"
        };
    }

    private sealed class LegacyCredentialFixture : IDisposable
    {
        private readonly string? _previousDataRoot;

        private LegacyCredentialFixture(
            string root,
            string baseDirectory,
            string machineConfigPath,
            string cachePath,
            string? previousDataRoot)
        {
            Root = root;
            BaseDirectory = baseDirectory;
            MachineConfigPath = machineConfigPath;
            CachePath = cachePath;
            _previousDataRoot = previousDataRoot;
        }

        public string Root { get; }
        public string BaseDirectory { get; }
        public string MachineConfigPath { get; }
        public string CachePath { get; }

        public static LegacyCredentialFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"iiot-launcher-credential-{Guid.NewGuid():N}");
            var baseDirectory = Path.Combine(root, "install", "current", "launcher");
            Directory.CreateDirectory(baseDirectory);
            var previous = Environment.GetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                Path.Combine(root, "program-data"));
            var pluginRoot = EdgeClientProgramDataPaths.ResolveDevicePluginRoot(
                RuntimeFixture.ClientCode,
                baseDirectory);
            var machineConfigPath = EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                RuntimeFixture.ClientCode,
                baseDirectory);
            WriteText(
                machineConfigPath,
                $$"""
                {
                  "InstanceId": "{{RuntimeFixture.ClientCode}}",
                  "Shell": {
                    "ClientCode": "{{RuntimeFixture.ClientCode}}",
                    "RuntimeDataRoot": "plugins/{{RuntimeFixture.ClientCode}}"
                  },
                  "CloudApi": {
                    "ClientCode": "{{RuntimeFixture.ClientCode}}",
                    "BootstrapSecret": "bootstrap-plaintext"
                  }
                }
                """);
            var cachePath = Path.Combine(pluginRoot, "device_cache.json");
            WriteText(
                cachePath,
                $$"""
                {
                  "ClientCode": "{{RuntimeFixture.ClientCode}}",
                  "DeviceId": "11111111-1111-1111-1111-111111111111",
                  "DeviceName": "P1",
                  "RefreshToken": "refresh-plaintext",
                  "UploadAccessToken": "access-plaintext"
                }
                """);
            return new LegacyCredentialFixture(
                root,
                baseDirectory,
                machineConfigPath,
                cachePath,
                previous);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                _previousDataRoot);
            DeleteDirectory(Root);
        }
    }

    private sealed class InMemoryCredentialStore : IEdgeCredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public int ReadCount { get; private set; }
        public void Write(string reference, string secret) => _values[reference] = secret;
        public string Read(string reference)
        {
            ReadCount++;
            return _values.TryGetValue(reference, out var value)
                ? value
                : throw new KeyNotFoundException(reference);
        }
        public void Delete(string reference) => _values.Remove(reference);
    }

    private sealed class FailingRoundTripCredentialStore : IEdgeCredentialStore
    {
        private readonly HashSet<string> _written = new(StringComparer.Ordinal);
        public void Write(string reference, string secret) => _written.Add(reference);
        public string Read(string reference)
            => _written.Contains(reference) ? "wrong-roundtrip" : throw new KeyNotFoundException(reference);
        public void Delete(string reference) => _written.Remove(reference);
    }

    private sealed class FixedSidProvider(string sid) : IEdgeCredentialOwnerSidProvider
    {
        public string GetCurrentOwnerSid() => sid;
    }

    private sealed class RecordingRuntimePreflight(List<string> calls) : ILauncherRuntimePreflight
    {
        public void ValidateIdentityBeforeWrites() => calls.Add("identity-before-writes");
        public void ValidateCompleteRuntime() => calls.Add("binding-v3-complete");
    }

    private sealed class RecordingCredentialMigrator(List<string> calls) : ILauncherLegacyCredentialMigrator
    {
        public void Migrate() => calls.Add("credential-migration");
    }

    private sealed class RecordingAccountInitializer(List<string> calls) : ILauncherAccountCatalogInitializer
    {
        public void EnsureCatalogExists() => calls.Add("host-db");
    }

    private sealed class RecordingUpdateInitializer(List<string> calls) : IEdgeUpdateConfigInitializer
    {
        public void EnsureConfigExists() => calls.Add("update-config");
        public bool TrySyncUpdateSource(string updateSource) => false;
    }

    private sealed class RecordingRecovery(List<string> calls) : IEdgeUpdateTransactionRecovery
    {
        public EdgeUpdateTransactionRecoveryResult RecoverPendingTransaction()
        {
            calls.Add("update-recovery");
            return new EdgeUpdateTransactionRecoveryResult(true, false, false);
        }

        public bool IsProfileBlocked(string machineProfile) => false;
    }

    private sealed class RecordingActivation(List<string> calls) : ILauncherPluginActivationReconciler
    {
        public void Reconcile() => calls.Add("legacy-activation");
        public bool IsReady(LauncherPluginActivation activation) => true;
    }

    private sealed class RecordingImporter(List<string> calls) : ILauncherDeviceBindingImporter
    {
        public void ApplyPendingBindings() => calls.Add("binding-v2-migration");
    }

    private sealed class NoOpLanguageService : IAppLanguageService
    {
        public System.Globalization.CultureInfo Current =>
            System.Globalization.CultureInfo.InvariantCulture;
        public LanguageOption CurrentOption => new(Current, "Invariant");
        public IReadOnlyList<LanguageOption> SupportedLanguages => [CurrentOption];
        public event EventHandler? LanguageChanged;
        public void Initialize() { }
        public void Change(System.Globalization.CultureInfo culture) => LanguageChanged?.Invoke(this, EventArgs.Empty);
        public string GetString(string key, string fallback = "") => fallback;
        public string Format(string key, string fallback, params object[] args) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, fallback, args);
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
