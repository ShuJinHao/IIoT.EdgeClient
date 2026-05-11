namespace IIoT.Edge.Module.Homogenization.Payload;

/// <summary>
/// 匀浆设备状态快照，由设备状态任务在 PLC 触发时读取，可用于 MES 状态上传和 Cloud 日志映射。
/// </summary>
public sealed class HomogenizationEquipmentStatusSnapshot
{
    /// <summary>
    /// 状态采集时间，用于 UI 最近状态和诊断记录。
    /// </summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// PLC 原始设备状态码，作为 MES status 字段和 Cloud 日志级别映射的来源。
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// 状态码对应的业务文本，由匀浆 code 配置映射。
    /// </summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// 状态消息列表，MES 上传和 Cloud 日志映射都只读取插件内生成的文本。
    /// </summary>
    public IReadOnlyList<string> Messages { get; set; } = [];
}
