namespace IIoT.Edge.Module.Stacking.Constants;

/// <summary>
/// 叠片模块运行时、诊断和上传状态使用的业务键，集中定义避免字符串散落。
/// </summary>
public static class StackingModuleConstants
{
    /// <summary>
    /// 叠片模块标识。
    /// </summary>
    public const string ModuleId = "Stacking";

    /// <summary>
    /// 叠片工序类型，写入 CellData 供 DataPipeline 路由。
    /// </summary>
    public const string ProcessType = "Stacking";

    /// <summary>
    /// 叠片 PLC 采集任务名称。
    /// </summary>
    public const string RuntimeTaskName = "Stacking.SignalCapture";

    /// <summary>
    /// 运行任务注册状态键，写入叠片运行上下文的扩展状态。
    /// </summary>
    public const string RuntimeRegisteredKey = "Stacking.RuntimeTaskRegistered";

    /// <summary>
    /// 最近一次从 PLC 读取到的叠片序号状态键。
    /// </summary>
    public const string LastObservedSequenceKey = "Stacking.LastObservedSequence";

    /// <summary>
    /// 最近一次从 PLC 读取到的叠片层数状态键。
    /// </summary>
    public const string LastObservedLayerCountKey = "Stacking.LastObservedLayerCount";

    /// <summary>
    /// 最近一次从 PLC 读取到的结果码状态键。
    /// </summary>
    public const string LastObservedResultCodeKey = "Stacking.LastObservedResultCode";

    /// <summary>
    /// 最近一次 PLC 采集时间状态键。
    /// </summary>
    public const string LastObservedAtKey = "Stacking.LastObservedAt";

    /// <summary>
    /// 最近一次发布到 DataPipeline 的叠片序号状态键。
    /// </summary>
    public const string LastPublishedSequenceKey = "Stacking.LastPublishedSequence";

    /// <summary>
    /// 最近一次发布到 DataPipeline 的电芯条码状态键。
    /// </summary>
    public const string LastPublishedBarcodeKey = "Stacking.LastPublishedBarcode";

    /// <summary>
    /// 叠片云端上传配置状态键。
    /// </summary>
    public const string CloudUploadConfiguredKey = "Stacking.CloudUploadConfigured";

    /// <summary>
    /// 最近一次 Cloud 上传状态键。
    /// </summary>
    public const string LastCloudUploadStatusKey = "Stacking.LastCloudUploadStatus";

    /// <summary>
    /// 最近一次 Cloud 上传时间键。
    /// </summary>
    public const string LastCloudUploadAtKey = "Stacking.LastCloudUploadAt";

    /// <summary>
    /// 最近一次 Cloud 上传失败原因键。
    /// </summary>
    public const string LastCloudUploadErrorKey = "Stacking.LastCloudUploadError";

    /// <summary>
    /// Cloud 上传成功状态值。
    /// </summary>
    public const string CloudUploadSuccessStatus = "Success";

    /// <summary>
    /// Cloud 上传失败状态值。
    /// </summary>
    public const string CloudUploadFailedStatus = "Failed";

    /// <summary>
    /// Cloud 上传被配置关闭的状态值。
    /// </summary>
    public const string CloudUploadDisabledStatus = "Disabled";
}
