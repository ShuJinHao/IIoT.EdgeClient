namespace IIoT.Edge.SharedKernel.DataPipeline;

/// <summary>
/// 失败记录实体，用于保存 SQLite 中待重传的数据。
/// 
/// 对应数据库：pipeline_cloud.db / pipeline_mes.db
/// 对应表：failed_cloud_records / failed_mes_records
/// 
/// ProcessQueueTask 消费失败时写入：
///   CellData 会序列化为 JSON，存入 CellDataJson
///   ProcessType 记录工序类型，反序列化时据此还原具体子类
///   Channel 为运行时附加字段，标识该记录由 Cloud 或 MES 通道负责。
/// 
/// RetryTask 捞出时：
///   根据 ProcessType 将 CellDataJson 反序列化为对应的 CellDataBase 子类
///   再包装成 CellCompletedRecord 继续消费
/// </summary>
public class FailedCellRecord
{
    public long Id { get; set; }

    /// <summary>
    /// 补传通道标识。
    /// "Cloud" 表示由 Cloud 通道的 RetryTask 负责。
    /// "MES" 表示由 MES 通道的 RetryTask 负责。
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// 工序类型标识（"Injection"、"DieCutting" 等）
    /// 反序列化时用于识别 CellDataJson 的具体类型
    /// </summary>
    public string ProcessType { get; set; } = string.Empty;

    /// <summary>
    /// CellDataBase 序列化后的 JSON；SQLite 仅保存字符串内容。
    /// </summary>
    public string CellDataJson { get; set; } = string.Empty;

    /// <summary>
    /// 标记失败发生在哪个消费者（如 "Cloud"、"MES"）。
    /// 重试时会从该步骤继续，之前已成功的步骤不会重复执行。
    /// </summary>
    public string FailedTarget { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public DateTime NextRetryTime { get; set; }
    public DateTime CreatedAt { get; set; }
}

