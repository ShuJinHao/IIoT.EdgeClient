namespace IIoT.Edge.Module.Stacking.Samples;

public sealed class StackingDevelopmentSampleOptions
{
    public const string SectionName = "DevelopmentSamples";

    public bool Enabled { get; set; }

    public bool SeedStackingModule { get; set; }

    public string StackingDeviceName { get; set; } = "PLC-STACKING-DEV";

    public string StackingIpAddress { get; set; } = "127.0.0.1";

    public int StackingPort { get; set; } = 1102;

    public string StackingPlcModel { get; set; } = "S7";

    public int StackingConnectTimeout { get; set; } = 1000;

    public string SampleBarcode { get; set; } = "ST-DEV-0001";

    public string SampleTrayCode { get; set; } = "TRAY-STACK-DEV";

    public int SampleLayerCount { get; set; } = 12;
}
