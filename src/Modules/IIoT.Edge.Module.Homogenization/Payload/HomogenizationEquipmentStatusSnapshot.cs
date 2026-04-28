namespace IIoT.Edge.Module.Homogenization.Payload;

public sealed class HomogenizationEquipmentStatusSnapshot
{
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    public int StatusCode { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public IReadOnlyList<string> Messages { get; set; } = [];
}
