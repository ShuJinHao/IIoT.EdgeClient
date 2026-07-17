namespace IIoT.Edge.SharedKernel.DataPipeline.CellData;

/// <summary>
/// 数据管道记录需要投递的目标通道。
/// </summary>
[Flags]
public enum DataPipelineUploadTargets
{
    None = 0,
    Mes = 1,
    Cloud = 2,
    All = Mes | Cloud
}

/// <summary>
/// 电芯生产数据基类。
/// 统一定义通用上下文字段，供各工序子类扩展。
/// </summary>
public abstract class CellDataBase
{
    /// <summary>
    /// 工序类型标识，例如 <c>TestProcess</c>。
    /// </summary>
    public abstract string ProcessType { get; }

    /// <summary>
    /// 面向列表和日志展示的标签文本。
    /// </summary>
    public virtual string DisplayLabel => ProcessType;

    /// <summary>
    /// 关联设备名称，对应生产上下文键。
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// 关联设备编码，兼容测试场景日志输出。
    /// </summary>
    public string DeviceCode { get; set; } = string.Empty;

    /// <summary>
    /// 兼容历史模型的字段：PLC 设备数据库 Id。
    /// </summary>
    public int? PlcDeviceId { get; set; }

    /// <summary>
    /// 工序判定结果：<c>true</c> / <c>false</c> / 未知。
    /// </summary>
    public bool? CellResult { get; set; }

    /// <summary>
    /// 任务完成时间，供入库与产能统计使用。
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 本记录应进入哪些上传通道。默认同时进入 MES 和 Cloud；仅 MES 的实时/状态类记录必须显式改为 <see cref="DataPipelineUploadTargets.Mes"/>。
    /// </summary>
    public DataPipelineUploadTargets UploadTargets { get; set; } = DataPipelineUploadTargets.All;
}
