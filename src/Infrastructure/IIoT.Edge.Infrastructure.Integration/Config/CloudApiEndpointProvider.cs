using Microsoft.Extensions.Options;

namespace IIoT.Edge.Infrastructure.Integration.Config;

/// <summary>
/// Resolves cloud API absolute URLs and client code from current config.
/// BaseUrl and ClientCode are read from IOptionsMonitor for runtime updates.
/// </summary>
public class CloudApiEndpointProvider : ICloudApiEndpointProvider
{
    private readonly IOptionsMonitor<CloudApiConfig> _cloudApiOptions;

    public CloudApiEndpointProvider(
        IOptionsMonitor<CloudApiConfig> cloudApiOptions)
    {
        _cloudApiOptions = cloudApiOptions;
    }

    public string BuildUrl(string relativeOrAbsoluteUrl)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.ToString();

        var baseUrl = _cloudApiOptions.CurrentValue.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Missing config: CloudApi:BaseUrl");

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException($"Invalid config: CloudApi:BaseUrl = '{baseUrl}'");

        return new Uri(baseUri, relativeOrAbsoluteUrl).ToString();
    }

    public string GetClientCode()
    {
        var configured = _cloudApiOptions.CurrentValue.ClientCode?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        throw new InvalidOperationException("Missing config: CloudApi:ClientCode");
    }

    public string GetDeviceInstancePath()
        => ResolvePath(_cloudApiOptions.CurrentValue.Paths.DeviceInstance, "/api/v1/edge/bootstrap/device-instance");

    public string GetIdentityDeviceLoginPath()
        => ResolvePath(_cloudApiOptions.CurrentValue.Paths.IdentityDeviceLogin, "/api/v1/human/identity/edge-login");

    public string GetDeviceLogPath()
        => ResolvePath(_cloudApiOptions.CurrentValue.Paths.DeviceLog, "/api/v1/edge/device-logs");

    public string GetCapacityHourlyPath()
        => ResolvePath(_cloudApiOptions.CurrentValue.Paths.CapacityHourly, "/api/v1/edge/capacity/hourly");

    public string GetCapacitySummaryPath()
        => ResolvePath(_cloudApiOptions.CurrentValue.Paths.CapacitySummary, "/api/v1/edge/capacity/summary");

    public string GetCapacitySummaryRangePath()
        => ResolvePath(_cloudApiOptions.CurrentValue.Paths.CapacitySummaryRange, "/api/v1/edge/capacity/summary/range");

    public string BuildRecipeByDevicePath(Guid deviceId)
    {
        var template = ResolvePath(
            _cloudApiOptions.CurrentValue.Paths.RecipeByDeviceTemplate,
            "/api/v1/edge/recipes/device/{deviceId}");

        return template.Replace("{deviceId}", deviceId.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePath(string? configured, string fallback)
    {
        var value = configured?.Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
