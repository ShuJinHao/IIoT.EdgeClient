namespace IIoT.Edge.Infrastructure.Integration.Config;

public class CloudApiConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSecs { get; set; } = 10;
    public string ClientCode { get; set; } = string.Empty;
    public string BootstrapSecret { get; set; } = string.Empty;
    public CloudApiPaths Paths { get; set; } = new();
}

public class CloudApiPaths
{
    public string DeviceInstance { get; set; } = string.Empty;
    public string BootstrapRefresh { get; set; } = string.Empty;
    public string IdentityDeviceLogin { get; set; } = string.Empty;
    public string HumanIdentityRefresh { get; set; } = string.Empty;
    public string DeviceLog { get; set; } = string.Empty;
    public string ProcessUpload { get; set; } = string.Empty;
    public string CapacityHourly { get; set; } = string.Empty;
    public string CapacitySummary { get; set; } = string.Empty;
    public string CapacitySummaryRange { get; set; } = string.Empty;
    public string RecipeByDeviceTemplate { get; set; } = string.Empty;
}
