namespace IIoT.Edge.Infrastructure.Integration.Config;

public class CloudApiConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSecs { get; set; } = 10;
    public string ClientCode { get; set; } = string.Empty;
    public CloudApiPaths Paths { get; set; } = new();
}

public class CloudApiPaths
{
    public string DeviceInstance { get; set; } = "/api/v1/edge/bootstrap/device-instance";
    public string IdentityDeviceLogin { get; set; } = "/api/v1/human/identity/edge-login";
    public string DeviceLog { get; set; } = "/api/v1/edge/device-logs";
    public string CapacityHourly { get; set; } = "/api/v1/edge/capacity/hourly";
    public string CapacitySummary { get; set; } = "/api/v1/edge/capacity/summary";
    public string CapacitySummaryRange { get; set; } = "/api/v1/edge/capacity/summary/range";
    public string RecipeByDeviceTemplate { get; set; } = "/api/v1/edge/recipes/device/{deviceId}";
}
