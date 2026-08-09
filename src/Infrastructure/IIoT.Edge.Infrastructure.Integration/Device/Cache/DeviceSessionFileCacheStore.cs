using System.Text.Json;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Infrastructure.Integration.Device.Cache;

public interface IDeviceSessionCacheStore
{
    void Save(DeviceSession session);

    DeviceSession? TryLoad(string clientCode);
}

public sealed class DeviceSessionCredentialMigrationException(
    string message,
    Exception innerException) : InvalidOperationException(message, innerException);

public class DeviceSessionFileCacheStore : IDeviceSessionCacheStore
{
    private readonly string _cacheFilePath;
    private readonly IEdgeCredentialStore? _credentialStore;

    public DeviceSessionFileCacheStore(
        string? cacheFilePath = null,
        IEdgeCredentialStore? credentialStore = null)
    {
        _cacheFilePath = cacheFilePath
            ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "device_cache.json");
        _credentialStore = credentialStore;
    }

    public void Save(DeviceSession session)
    {
        var directory = Path.GetDirectoryName(_cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var normalizedClientCode = IIoT.Edge.SharedKernel.Configuration.EdgeClientIdentity.NormalizeClientCode(
            session.ClientCode);
        string? refreshReference = null;
        if (!string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            if (_credentialStore is null)
            {
                throw new InvalidOperationException(
                    "A credential store is required to persist a refresh token.");
            }

            refreshReference = WindowsCredentialManagerStore.CreateSessionReference(normalizedClientCode);
            _credentialStore.Write(refreshReference, session.RefreshToken);
            if (!string.Equals(
                    _credentialStore.Read(refreshReference),
                    session.RefreshToken,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Refresh credential round-trip validation failed.");
            }
        }

        var model = new DeviceSessionCacheModel
        {
            SessionKind = "Active",
            GenerationId = session.GenerationId,
            DeviceId = session.DeviceId,
            DeviceName = session.DeviceName,
            ClientCode = normalizedClientCode,
            ProcessId = session.ProcessId,
            RefreshCredentialReference = refreshReference,
            RefreshTokenExpiresAtUtc = session.RefreshTokenExpiresAtUtc
        };
        WriteAtomically(JsonSerializer.Serialize(model));
    }

    public DeviceSession? TryLoad(string clientCode)
    {
        if (!File.Exists(_cacheFilePath))
        {
            return null;
        }

        var json = File.ReadAllText(_cacheFilePath);
        var cacheModel = JsonSerializer.Deserialize<DeviceSessionCacheModel>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (cacheModel is null
            || cacheModel.DeviceId == Guid.Empty
            || string.IsNullOrWhiteSpace(cacheModel.DeviceName))
        {
            return null;
        }

        var cachedClientCode = cacheModel.ClientCode?.Trim();
        var isLegacyCache = string.IsNullOrWhiteSpace(cachedClientCode);

        if (!isLegacyCache
            && !string.Equals(cachedClientCode, clientCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var refreshReference = cacheModel.RefreshCredentialReference?.Trim();
        var refreshToken = string.IsNullOrWhiteSpace(refreshReference)
            ? cacheModel.RefreshToken
            : (_credentialStore ?? throw new InvalidOperationException(
                "A credential store is required to read a refresh-token reference."))
                .Read(refreshReference);
        var session = new DeviceSession
        {
            SessionKind = string.IsNullOrWhiteSpace(cacheModel.SessionKind)
                ? "Active"
                : cacheModel.SessionKind,
            GenerationId = cacheModel.GenerationId,
            DeviceId = cacheModel.DeviceId,
            DeviceName = cacheModel.DeviceName,
            ClientCode = clientCode,
            ProcessId = cacheModel.ProcessId,
            // Access and activation tokens are never restored from disk.
            UploadAccessToken = null,
            UploadAccessTokenExpiresAtUtc = null,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAtUtc = cacheModel.RefreshTokenExpiresAtUtc
        };

        if (isLegacyCache
            || !string.IsNullOrWhiteSpace(cacheModel.UploadAccessToken)
            || !string.IsNullOrWhiteSpace(cacheModel.RefreshToken))
        {
            try
            {
                Save(session);
            }
            catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or InvalidDataException
                                           or InvalidOperationException
                                           or PlatformNotSupportedException
                                           or System.ComponentModel.Win32Exception)
            {
                throw new DeviceSessionCredentialMigrationException(
                    "Legacy plaintext device credential migration failed; startup is blocked and the source file was preserved.",
                    ex);
            }
        }

        return session;
    }

    private sealed class DeviceSessionCacheModel
    {
        public string SessionKind { get; init; } = "Active";
        public string? GenerationId { get; init; }
        public Guid DeviceId { get; init; }
        public string DeviceName { get; init; } = string.Empty;
        public string? ClientCode { get; init; }
        public string? MacAddress { get; init; }
        public Guid ProcessId { get; init; }
        public string? UploadAccessToken { get; init; }
        public DateTimeOffset? UploadAccessTokenExpiresAtUtc { get; init; }
        public string? RefreshToken { get; init; }
        public string? RefreshCredentialReference { get; init; }
        public DateTimeOffset? RefreshTokenExpiresAtUtc { get; init; }
    }

    private void WriteAtomically(string json)
    {
        var directory = Path.GetDirectoryName(_cacheFilePath)
            ?? throw new InvalidOperationException("Device cache directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_cacheFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _cacheFilePath, overwrite: true);
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
