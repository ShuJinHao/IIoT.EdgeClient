namespace IIoT.Edge.Presentation.VisualTestData;

/// <summary>
/// UI 视觉验收测试数据开关。该开关只替换展示层查询源，不允许承载真实生产链路逻辑。
/// </summary>
public sealed class VisualTestDataOptions
{
    public const string SectionName = "UI:VisualTestData";

    /// <summary>
    /// 是否启用视觉验收测试数据源。生产默认必须关闭。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 测试数据主设备名称，用于多个页面保持同一视觉上下文。
    /// </summary>
    public string PrimaryDeviceName { get; set; } = "PLC-Homogenization-01";

    /// <summary>
    /// 测试批次号，仅用于界面展示验收。
    /// </summary>
    public string BatchCode { get; set; } = "VT-HG-20260602-01";
}
