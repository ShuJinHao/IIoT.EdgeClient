using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Features.Production.Planning;
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

    public string? LastOutboundFingerprint { get; set; }

    public string? LastProductionRecordFingerprint { get; set; }

    public string? LastDeviceStatusFingerprint { get; set; }

    /// <summary>
    /// 最近一次模切设备状态处理时间。
    /// </summary>
    public DateTime? LastDeviceStatusAt { get; set; }

    /// <summary>
    /// 最近一次模切设备状态处理结果。
    /// </summary>
    public string? LastDeviceStatusResult { get; set; }

    /// <summary>
    /// 最近一次模切采样上传结果。
    /// </summary>
    public string? LastRealtimeResult { get; set; }

    /// <summary>
    /// 下一轮采样窗口开始时间。
    /// </summary>
    public DateTime? NextWindowStartAt { get; set; }

    /// <summary>
    /// 当前已选择的 MES 主批计划。
    /// </summary>
    public ProductionPlanOption? SelectedProductionPlan { get; set; }

    /// <summary>
    /// 本次软件启动后选择主批计划生成的运行会话号；不跨进程复用。
    /// </summary>
    public string? PlanSessionId { get; set; }

    /// <summary>
    /// 当前主批计划对应的 MES 追溯批次号。
    /// </summary>
    public string? TraceBatchNumber { get; set; }

    /// <summary>
    /// 最近一次追溯批次号生成时间。
    /// </summary>
    public DateTime? TraceBatchGeneratedAt { get; set; }

    /// <summary>
    /// 最近一次追溯批次号生成错误。
    /// </summary>
    public string? TraceBatchError { get; set; }
}

/// <summary>
/// 模切运行上下文工厂，按单台 PLC 设备创建上下文。
/// </summary>
internal sealed class DieCuttingContextFactory : IProductionContextFactory
{
    private readonly DieCuttingModuleDefinition _definition;

    public DieCuttingContextFactory(DieCuttingModuleDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    /// <summary>
    /// 当前工厂所属模切模块标识。
    /// </summary>
    public string ModuleId => _definition.ModuleId;

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
