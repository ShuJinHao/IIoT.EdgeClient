using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Runtime.Base;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

/// <summary>
/// 心跳镜像任务：周期读取 PLC 输入心跳并写回输出心跳。
/// </summary>
internal sealed class HomogenizationHeartbeatTask : HeartbeatMirrorPlcTaskBase
{
    private readonly HomogenizationContext _context;
    private readonly IProductionTimeProvider _productionTime;
    private readonly int _taskLoopInterval;
    private HomogenizationSignalCodec? _codec;

    public HomogenizationHeartbeatTask(
        IPlcBuffer buffer,
        HomogenizationContext context,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, context, logger)
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

    /// <summary>
    /// PLC 输入心跳信号标签。
    /// </summary>
    protected override string InputLabel => HomogenizationPlcSignalProfile.HeartbeatIn.Label;

    /// <summary>
    /// 上位机写回 PLC 的输出心跳信号标签。
    /// </summary>
    protected override string OutputLabel => HomogenizationPlcSignalProfile.HeartbeatOut.Label;

    protected override ushort ReadWord(string label)
        => Codec.ReadWord(label);

    protected override void WriteWord(string label, ushort value)
        => Codec.WriteWord(label, value);

    protected override Task OnHeartbeatMirroredAsync(
        ushort input,
        ushort output,
        CancellationToken cancellationToken)
    {
        _context.LastHeartbeatAt = _productionTime.BusinessNow;
        return Task.CompletedTask;
    }

    private HomogenizationSignalCodec Codec => _codec ??= new HomogenizationSignalCodec(Buffer, _context, _productionTime);
}
