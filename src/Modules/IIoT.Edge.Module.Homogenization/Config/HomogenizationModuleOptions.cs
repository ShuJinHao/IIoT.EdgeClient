namespace IIoT.Edge.Module.Homogenization.Config;

/// <summary>
/// 匀浆模块运行和界面配置。
/// </summary>
public sealed class HomogenizationModuleOptions
{
    /// <summary>
    /// 界面展示相关配置。
    /// </summary>
    public HomogenizationPresentationOptions Presentation { get; set; } = new();

    /// <summary>
    /// PLC 任务循环相关配置。
    /// </summary>
    public HomogenizationRuntimeOptions Runtime { get; set; } = new();
}

/// <summary>
/// 匀浆模块界面刷新和缓存配置。
/// </summary>
public sealed class HomogenizationPresentationOptions
{
    /// <summary>
    /// 数据页面刷新间隔，单位毫秒。
    /// </summary>
    public int DataViewRefreshIntervalMs { get; set; } = 1000;

    /// <summary>
    /// UI 内存中保留的最近出料记录上限。
    /// </summary>
    public int MaxOutboundRecords { get; set; } = 500;
}

/// <summary>
/// 匀浆 PLC 任务循环配置。
/// </summary>
public sealed class HomogenizationRuntimeOptions
{
    /// <summary>
    /// 触发-应答、心跳任务循环间隔，单位毫秒。
    /// </summary>
    public int EventLoopIntervalMs { get; set; } = 50;

    /// <summary>
    /// 实时快照上传任务循环间隔，单位毫秒。
    /// </summary>
    public int RealtimeLoopIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 触发-应答、心跳任务允许的最小循环间隔，单位毫秒。
    /// </summary>
    public int MinEventLoopIntervalMs { get; set; } = 20;

    /// <summary>
    /// 实时快照上传任务允许的最小循环间隔，单位毫秒。
    /// </summary>
    public int MinRealtimeLoopIntervalMs { get; set; } = 200;
}
