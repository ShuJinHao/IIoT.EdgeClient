using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Device;

namespace IIoT.Edge.Infrastructure.Integration.Device.Cache;

public class DeviceSessionFileCacheStore
{
    private readonly string _cacheFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "device_cache.json");

    public void Save(DeviceSession session)
    {
        var json = JsonSerializer.Serialize(session);
        File.WriteAllText(_cacheFilePath, json);
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

        var session = new DeviceSession
        {
            DeviceId = cacheModel.DeviceId,
            DeviceName = cacheModel.DeviceName,
            ClientCode = clientCode,
            ProcessId = cacheModel.ProcessId,
            UploadAccessToken = cacheModel.UploadAccessToken,
            UploadAccessTokenExpiresAtUtc = cacheModel.UploadAccessTokenExpiresAtUtc
        };

        if (isLegacyCache)
        {
            Save(session);
        }

        return session;
    }

    private sealed class DeviceSessionCacheModel
    {
        public Guid DeviceId { get; init; }
        public string DeviceName { get; init; } = string.Empty;
        public string? ClientCode { get; init; }
        public string? MacAddress { get; init; }
        public Guid ProcessId { get; init; }
        public string? UploadAccessToken { get; init; }
        public DateTimeOffset? UploadAccessTokenExpiresAtUtc { get; init; }
    }
}
