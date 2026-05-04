namespace IIoT.Edge.Module.Homogenization.Payload;

/// <summary>
/// 匀浆设备状态快照，由设备状态任务在 PLC 触发时读取状态码并上传到 MES 设备状态接口。
/// </summary>
public sealed class HomogenizationEquipmentStatusSnapshot
{
    /// <summary>
    /// 状态采集时间，用于 UI 最近状态和诊断记录。
    /// </summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// PLC 原始设备状态码，直接作为 MES status 字段来源。
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// 状态码对应的业务文本，由匀浆 MES code 配置映射。
    /// </summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// 上传给 MES 的状态消息列表，当前包含状态文本，后续异常信息也应留在插件内扩展。
    /// </summary>
    public IReadOnlyList<string> Messages { get; set; } = [];
}
