using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Module.Sdk.Base;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

/// <summary>
/// 心跳镜像任务：周期读取 PLC 输入心跳并写回输出心跳。
/// </summary>
internal sealed class HomogenizationHeartbeatTask : HeartbeatMirrorPlcTaskBase<HomogenizationPlcSignals.Interaction>
{
    private readonly HomogenizationContext _context;
    private readonly IProductionTimeProvider _productionTime;
    private readonly int _taskLoopInterval;

    /// <summary>
    /// 创建匀浆心跳镜像任务，心跳点位属于信号交互枚举但不使用触发/应答业务码。
    /// </summary>
    public HomogenizationHeartbeatTask(
        IPlcBuffer buffer,
        ILogicalSignalAccessor<HomogenizationPlcSignals.Interaction> signals,
        HomogenizationContext context,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions)
        : base(buffer, signals, context, logger)
    {
        _context = context;
        _productionTime = productionTime;
        var runtime = moduleOptions.Value.Runtime;
        _taskLoopInterval = Math.Max(runtime.MinEventLoopIntervalMs, runtime.EventLoopIntervalMs);
    }

    /// <summary>
    /// 心跳镜像任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Heartbeat";

    /// <summary>
    /// 心跳镜像循环间隔，复用匀浆触发-应答任务间隔配置。
    /// </summary>
    protected override int TaskLoopInterval => _taskLoopInterval;

    protected override HomogenizationPlcSignals.Interaction InputSignal
        => HomogenizationPlcSignals.Interaction.心跳;

    protected override HomogenizationPlcSignals.Interaction OutputSignal
        => HomogenizationPlcSignals.Interaction.心跳;

    protected override Task OnHeartbeatMirroredAsync(
        ushort input,
        ushort output,
        CancellationToken cancellationToken)
    {
        _context.LastHeartbeatAt = _productionTime.BusinessNow;
        return Task.CompletedTask;
    }
}
