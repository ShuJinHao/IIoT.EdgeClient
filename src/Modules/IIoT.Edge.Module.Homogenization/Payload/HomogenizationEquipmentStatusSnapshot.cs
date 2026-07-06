namespace IIoT.Edge.Module.Homogenization.Payload;

/// <summary>
/// 匀浆设备状态快照，由设备状态任务在 PLC 触发时读取，用于设备状态上传链路。
/// </summary>
public sealed class HomogenizationEquipmentStatusSnapshot
{
    /// <summary>
    /// 状态采集时间，用于 UI 最近状态和诊断记录。
    /// </summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// PLC 原始设备状态码，作为设备状态上传的 status 字段来源。
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// 状态码对应的业务文本，由匀浆 code 配置映射。
    /// </summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// 状态消息列表，由插件内解码生成并随设备状态记录进入 DataPipeline。
    /// </summary>
    public IReadOnlyList<string> Messages { get; set; } = [];
}
