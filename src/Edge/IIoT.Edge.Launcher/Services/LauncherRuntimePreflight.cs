using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherRuntimePreflight
{
    void ValidateIdentityBeforeWrites();

    void ValidateCompleteRuntime();
}

public static class LauncherRuntimeMode
{
    public const string DevelopmentFixturesEnvironmentVariable =
        "IIOT_EDGE_ENABLE_DEVELOPMENT_FIXTURES";

    public static bool DevelopmentFixturesAreEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable(DevelopmentFixturesEnvironmentVariable),
            "1",
            StringComparison.Ordinal);
}

/// <summary>
/// Performs the read-only production guard before host.db is touched, then validates the
/// Installer-owned v3 runtime projection, credential ownership and exact installed plugin bytes.
/// It never repairs, supplements or materializes a Binding.
/// </summary>
public sealed class LauncherRuntimePreflight(
    string baseDirectory,
    ILauncherProfileCatalog profileCatalog,
    IEdgeCredentialStore credentialStore,
    IEdgeCredentialOwnerSidProvider credentialOwnerSidProvider)
    : ILauncherRuntimePreflight
{
    private static readonly HashSet<string> ForbiddenSecretPropertyNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "pendingCredentialSecret",
        "bootstrapSecret",
        "refreshToken",
        "accessToken",
        "uploadAccessToken"
    };

    public void ValidateIdentityBeforeWrites()
    {
        var runtimePath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(baseDirectory);
        if (!File.Exists(runtimePath))
        {
            if (LauncherRuntimeMode.DevelopmentFixturesAreEnabled()
                || File.Exists(ResolveLegacyPendingBindingPath()))
            {
                return;
            }

            throw new InvalidDataException(
                "LAUNCHER_PRODUCTION_BINDING_REQUIRED: production startup requires Installer-owned Binding v3.");
        }

        var envelope = ReadRuntime(runtimePath);
        if (envelope.SchemaVersion == EdgeInstallerBindingCodec.CurrentSchemaVersion)
        {
            ValidateCredentialOwner(envelope);
        }
    }

    public void ValidateCompleteRuntime()
    {
        var runtimePath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(baseDirectory);
        if (!File.Exists(runtimePath))
        {
            if (LauncherRuntimeMode.DevelopmentFixturesAreEnabled())
            {
                _ = profileCatalog.LoadProfiles();
                return;
            }

            throw new InvalidDataException(
                "LAUNCHER_PRODUCTION_BINDING_REQUIRED: production startup requires Installer-owned Binding v3.");
        }

        var json = File.ReadAllText(runtimePath);
        using (var document = JsonDocument.Parse(json))
        {
            RejectSecretProperties(document.RootElement);
        }

        var envelope = EdgeInstallerBindingCodec.ParseRuntime(json);
        if (envelope.SchemaVersion != EdgeInstallerBindingCodec.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                "LAUNCHER_BINDING_V3_REQUIRED: Binding v2 is migration input only and cannot start a production Shell.");
        }

        ValidateCredentialOwner(envelope);
        var payload = ToValidationPayload(envelope);
        foreach (var runtimeBinding in envelope.Bindings)
        {
            var secret = credentialStore.Read(runtimeBinding.PendingCredentialReference);
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidDataException(
                    "LAUNCHER_BINDING_CREDENTIAL_UNAVAILABLE: a Binding credential reference is empty.");
            }

            var binding = payload.Bindings.Single(candidate => string.Equals(
                candidate.ClientCode,
                runtimeBinding.ClientCode,
                StringComparison.Ordinal));
            var machineConfigPath = EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                runtimeBinding.ClientCode,
                baseDirectory);
            if (!File.Exists(machineConfigPath))
            {
                throw new FileNotFoundException(
                    "LAUNCHER_BINDING_MACHINE_CONFIG_MISSING: Installer materialized configuration is missing.",
                    machineConfigPath);
            }

            var machineJson = File.ReadAllText(machineConfigPath);
            using (var document = JsonDocument.Parse(machineJson))
            {
                RejectSecretProperties(document.RootElement);
            }

            var root = JsonNode.Parse(machineJson)?.AsObject()
                ?? throw new InvalidDataException(
                    "LAUNCHER_BINDING_MACHINE_CONFIG_INVALID: machine configuration root must be an object.");
            EdgeBindingMaterializer.ValidateV3(
                root,
                payload,
                binding,
                $"plugins/{runtimeBinding.ClientCode}",
                runtimeBinding.PluginDirectory);
        }

        var profiles = profileCatalog.LoadProfiles();
        if (profiles.Count != envelope.Bindings.Count
            || envelope.Bindings.Any(binding => !profiles.Any(profile =>
                string.Equals(profile.ClientCode, binding.ClientCode, StringComparison.Ordinal)
                && string.Equals(profile.PluginVersion, binding.PluginVersion, StringComparison.Ordinal)
                && string.Equals(profile.PackageSha256, binding.PackageSha256, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException(
                "LAUNCHER_PLUGIN_PREFLIGHT_MISMATCH: runtime profiles do not match Binding v3.");
        }
    }

    private EdgeRuntimeBindingEnvelope ReadRuntime(string runtimePath)
    {
        var json = File.ReadAllText(runtimePath);
        using (var document = JsonDocument.Parse(json))
        {
            RejectSecretProperties(document.RootElement);
        }

        return EdgeInstallerBindingCodec.ParseRuntime(json);
    }

    private void ValidateCredentialOwner(EdgeRuntimeBindingEnvelope envelope)
    {
        var currentSid = WindowsCredentialOwnerSidProvider.Validate(
            credentialOwnerSidProvider.GetCurrentOwnerSid());
        if (envelope.Bindings.Any(binding => !string.Equals(
                binding.CredentialOwnerSid,
                currentSid,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "LAUNCHER_CREDENTIAL_OWNER_SID_MISMATCH: reinstall with the Windows account that runs Launcher.");
        }
    }

    private string ResolveLegacyPendingBindingPath()
        => Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory),
            LauncherDeviceBindingImporter.BindingFileName);

    private static EdgeInstallerBindingEnvelope ToValidationPayload(
        EdgeRuntimeBindingEnvelope envelope)
        => new(
            envelope.SchemaVersion,
            envelope.GenerationId,
            envelope.GeneratedAtUtc,
            envelope.ExpiresAtUtc,
            envelope.BaseUrl,
            envelope.Paths,
            envelope.Bindings.Select(binding => new EdgeInstallerDeviceBinding(
                binding.ClientCode,
                binding.DeviceName,
                binding.ProcessId,
                binding.ProcessType,
                binding.ModuleId,
                binding.PluginVersion,
                binding.PackageSha256,
                binding.PluginDirectory,
                binding.ConfigDirectory,
                binding.DbDirectory,
                binding.DataDirectory,
                binding.LogsDirectory,
                binding.CacheDirectory,
                binding.ContextDirectory,
                binding.BuffersDirectory,
                binding.PendingCredentialReference,
                "runtime-secret-not-materialized")).ToArray());

    private static void RejectSecretProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ForbiddenSecretPropertyNames.Contains(property.Name))
                    {
                        throw new InvalidDataException(
                            "LAUNCHER_RUNTIME_SECRET_PROPERTY_FORBIDDEN: runtime files must not contain Cloud secrets.");
                    }

                    RejectSecretProperties(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    RejectSecretProperties(item);
                }

                break;
        }
    }
}
