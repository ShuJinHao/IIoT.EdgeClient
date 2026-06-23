using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Module.DieCutting.Payload;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.DieCutting.Production;

/// <summary>
/// 模切运行态上下文，保存最近一次只读采样和 MES 上传结果。
/// </summary>
public sealed class DieCuttingContext : ProductionContext
{
    /// <summary>
    /// 最近一次模切采样快照。
    /// </summary>
    public DieCuttingRealtimeSnapshot? LastRealtimeSnapshot { get; set; }

    /// <summary>
    /// 最近一次模切采样上传时间。
    /// </summary>
    public DateTime? LastRealtimeAt { get; set; }

    /// <summary>
    /// 最近一次模切采样上传结果。
    /// </summary>
    public string? LastRealtimeResult { get; set; }

    /// <summary>
    /// 下一轮采样窗口开始时间。
    /// </summary>
    public DateTime? NextWindowStartAt { get; set; }
}

/// <summary>
/// 模切运行上下文工厂，按单台 PLC 设备创建上下文。
/// </summary>
internal sealed class DieCuttingContextFactory : IProductionContextFactory
{
    /// <summary>
    /// 当前工厂所属模切模块标识。
    /// </summary>
    public string ModuleId => DependencyInjection.ModuleKey;

    /// <summary>
    /// 宿主可创建的模切上下文类型。
    /// </summary>
    public Type ContextType => typeof(DieCuttingContext);

    public ProductionContext Create(string deviceName)
        => new DieCuttingContext
        {
            DeviceName = deviceName
        };
}
