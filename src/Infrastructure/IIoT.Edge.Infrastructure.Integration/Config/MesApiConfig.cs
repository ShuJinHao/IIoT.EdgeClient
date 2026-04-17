namespace IIoT.Edge.Infrastructure.Integration.Config;

public sealed class MesApiConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSecs { get; set; } = 10;
    public Dictionary<string, string> DefaultHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
