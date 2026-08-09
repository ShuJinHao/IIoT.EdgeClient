using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Infrastructure.Integration.Device;

internal sealed class RuntimeBindingActivationStateStore(
    string baseDirectory,
    IEdgeCredentialStore credentialStore) : IDeviceActivationStateStore
{
    public bool IsActivated(string clientCode, string generationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(generationId);
        var path = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(baseDirectory);
        if (!File.Exists(path))
        {
            return false;
        }

        var envelope = EdgeInstallerBindingCodec.ParseRuntime(File.ReadAllText(path));
        if (!string.Equals(envelope.GenerationId, generationId.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedClientCode = EdgeClientIdentity.NormalizeClientCode(clientCode);
        var matches = envelope.Bindings
            .Where(binding => string.Equals(
                binding.ClientCode,
                normalizedClientCode,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1
               && string.Equals(matches[0].ActivationStatus, "Activated", StringComparison.Ordinal);
    }

    public void CommitActivated(DeviceSession activeSession, string generationId)
        => CommitStatus(activeSession, generationId, "Activated", removePendingCredential: true);

    public void CommitActivating(DeviceSession activeSession, string generationId)
        => CommitStatus(activeSession, generationId, "Activating", removePendingCredential: false);

    private void CommitStatus(
        DeviceSession activeSession,
        string generationId,
        string status,
        bool removePendingCredential)
    {
        ArgumentNullException.ThrowIfNull(activeSession);
        ArgumentException.ThrowIfNullOrWhiteSpace(generationId);
        var path = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(baseDirectory);
        if (!File.Exists(path))
        {
            // Legacy v2 installations have no runtime binding activation ledger.
            return;
        }

        var envelope = EdgeInstallerBindingCodec.ParseRuntime(File.ReadAllText(path));
        if (!string.Equals(envelope.GenerationId, generationId.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Activation generation does not match runtime binding.");
        }

        var normalizedClientCode = EdgeClientIdentity.NormalizeClientCode(activeSession.ClientCode);
        var matches = envelope.Bindings
            .Where(binding => string.Equals(
                binding.ClientCode,
                normalizedClientCode,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException("Activation ClientCode is not unique in runtime binding.");
        }

        var reference = matches[0].PendingCredentialReference;
        var updated = envelope with
        {
            Bindings = envelope.Bindings.Select(binding => string.Equals(
                binding.ClientCode,
                normalizedClientCode,
                StringComparison.OrdinalIgnoreCase)
                ? binding with { ActivationStatus = status }
                : binding).ToArray()
        };
        WriteAtomically(path, EdgeInstallerBindingCodec.SerializeRuntime(updated));

        if (removePendingCredential)
        {
            // Only Cloud's second-phase confirmation invalidates the pending generation.
            // Keep it throughout Activating so a lost confirm response can be replayed.
            credentialStore.Delete(reference);
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Runtime binding directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, new System.Text.UTF8Encoding(false));
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
