using IIoT.Edge.Infrastructure.Integration.Config;

namespace IIoT.Edge.Testing;

public sealed class FakeCloudApiEndpointProvider : ICloudApiEndpointProvider
{
    private static readonly Uri BaseUri = new("https://cloud.test");

    public string BuildUrl(string relativeOrAbsoluteUrl)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absoluteUri)
            && (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri.ToString();
        }

        return new Uri(BaseUri, relativeOrAbsoluteUrl.TrimStart('/')).ToString();
    }

    public string GetClientCode() => "TEST";
    public string GetBootstrapSecret() => "bootstrap-secret";
    public string GetDeviceInstancePath() => "/api/v1/bootstrap/device-instance";
    public string GetBootstrapRefreshPath() => "/api/v1/bootstrap/edge-refresh";
    public string GetIdentityDeviceLoginPath() => "/api/v1/bootstrap/edge-login";
    public string GetHumanIdentityRefreshPath() => "/api/v1/human/identity/refresh";
    public string GetDeviceLogPath() => "/api/v1/edge/device-logs";
    public string GetEdgeHostPlcRuntimeStatesPath() => "/api/v1/edge/edge-hosts/plc-runtime-states";
    public string GetPassStationBatchPath(string typeKey) => $"/api/v1/edge/pass-stations/{typeKey}/batch";
    public string BuildRecipeByDevicePath(Guid deviceId) => $"/api/v1/edge/recipes/device/{deviceId}";
    public string GetCapacityHourlyPath() => "/api/v1/edge/capacity/hourly";
    public string GetCapacitySummaryPath() => "/api/v1/edge/capacity/summary";
    public string GetCapacitySummaryRangePath() => "/api/v1/edge/capacity/summary/range";
}
