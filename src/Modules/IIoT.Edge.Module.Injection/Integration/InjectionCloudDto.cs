namespace IIoT.Edge.Module.Injection.Integration;

public class InjectionCloudDto
{
    public string Barcode { get; set; } = string.Empty;
    public string CellResult { get; set; } = string.Empty;
    public DateTime CompletedTime { get; set; }
    public DateTime PreInjectionTime { get; set; }
    public double PreInjectionWeight { get; set; }
    public DateTime PostInjectionTime { get; set; }
    public double PostInjectionWeight { get; set; }
    public double InjectionVolume { get; set; }
}
