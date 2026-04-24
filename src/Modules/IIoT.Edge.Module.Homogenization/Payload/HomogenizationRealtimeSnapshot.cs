namespace IIoT.Edge.Module.Homogenization.Payload;

public sealed class HomogenizationRealtimeSnapshot
{
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    public short StirringSpeed { get; set; }

    public short StirringCurrent { get; set; }

    public short DispersionSpeed { get; set; }

    public short DispersionCurrent { get; set; }

    public short Temperature { get; set; }

    public short Vacuum { get; set; }
}
