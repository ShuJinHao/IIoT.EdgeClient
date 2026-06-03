namespace IIoT.Edge.Module.Homogenization.Config;

/// <summary>
/// 匀浆 PLC 和 MES 码表配置。
/// </summary>
public sealed class HomogenizationCodeOptions
{
    /// <summary>
    /// PLC 触发码和应答码配置。
    /// </summary>
    public HomogenizationPlcCodeOptions Plc { get; set; } = new();

    /// <summary>
    /// MES 通道和字段码表配置。
    /// </summary>
    public HomogenizationMesCodeOptions Mes { get; set; } = new();

    /// <summary>
    /// Cloud 日志映射码表配置。
    /// </summary>
    public HomogenizationCloudCodeOptions Cloud { get; set; } = new();
}
