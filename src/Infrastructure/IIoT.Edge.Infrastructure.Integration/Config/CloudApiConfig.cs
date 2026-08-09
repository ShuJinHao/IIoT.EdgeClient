namespace IIoT.Edge.Infrastructure.Integration.Config;

public class CloudApiConfig
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSecs { get; set; } = 10;
    public string ClientCode { get; set; } = string.Empty;
    public string BootstrapSecret { get; set; } = string.Empty;
    public string BootstrapCredentialReference { get; set; } = string.Empty;
    public CloudApiPaths Paths { get; set; } = new();
}

public class CloudApiPaths
{
    public string DeviceInstance { get; set; } = string.Empty;
    public string BootstrapRefresh { get; set; } = string.Empty;
    public string ActivateDevice { get; set; } = string.Empty;
    public string ActivateDeviceConfirm { get; set; } = string.Empty;
    public string IdentityDeviceLogin { get; set; } = string.Empty;
    public string HumanIdentityRefresh { get; set; } = string.Empty;
    public string HumanSessionValidation { get; set; } = string.Empty;
    public string DeviceLog { get; set; } = string.Empty;
    public string PassStationBatchTemplate { get; set; } = string.Empty;
    public string CapacityHourly { get; set; } = string.Empty;
    public string CapacitySummary { get; set; } = string.Empty;
    public string CapacitySummaryRange { get; set; } = string.Empty;
    public string RecipeByDeviceTemplate { get; set; } = string.Empty;
    public string ClientReleaseCatalogTemplate { get; set; } = string.Empty;
    public string ClientVersionReport { get; set; } = string.Empty;
    public string RuntimeHeartbeat { get; set; } = string.Empty;
    public string EdgeHostPlcRuntimeStates { get; set; } = string.Empty;
}
