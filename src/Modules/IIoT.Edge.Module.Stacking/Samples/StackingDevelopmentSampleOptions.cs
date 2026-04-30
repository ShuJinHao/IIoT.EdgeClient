namespace IIoT.Edge.Module.Stacking.Samples;

/// <summary>
/// 叠片开发样本配置，只在 Development 环境下用于生成演示 PLC、IO 映射和运行态样本。
/// </summary>
public sealed class StackingDevelopmentSampleOptions
{
    /// <summary>
    /// 样本配置节名称。
    /// </summary>
    public const string SectionName = "DevelopmentSamples";

    /// <summary>
    /// 是否启用开发样本总开关。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 是否生成叠片模块样本设备和数据。
    /// </summary>
    public bool SeedStackingModule { get; set; }

    /// <summary>
    /// 开发样本 PLC 设备名。
    /// </summary>
    public string StackingDeviceName { get; set; } = "PLC-STACKING-DEV";

    /// <summary>
    /// 开发样本 PLC IP 地址。
    /// </summary>
    public string StackingIpAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// 开发样本 PLC 端口。
    /// </summary>
    public int StackingPort { get; set; } = 1102;

    /// <summary>
    /// 开发样本 PLC 协议型号。
    /// </summary>
    public string StackingPlcModel { get; set; } = "S7";

    /// <summary>
    /// 开发样本 PLC 连接超时，单位毫秒。
    /// </summary>
    public int StackingConnectTimeout { get; set; } = 1000;

    /// <summary>
    /// 运行态样本电芯条码。
    /// </summary>
    public string SampleBarcode { get; set; } = "ST-DEV-0001";

    /// <summary>
    /// 运行态样本托盘码。
    /// </summary>
    public string SampleTrayCode { get; set; } = "TRAY-STACK-DEV";

    /// <summary>
    /// 运行态样本叠片层数。
    /// </summary>
    public int SampleLayerCount { get; set; } = 12;
}
