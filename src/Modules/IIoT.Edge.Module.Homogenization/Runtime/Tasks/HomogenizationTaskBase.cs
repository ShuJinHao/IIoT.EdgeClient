using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Runtime.Base;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

/// <summary>
/// 匀浆握手类任务基类，统一提供 PLC 信号码表、循环间隔和 MES 结果到 PLC 应答码的转换。
/// </summary>
internal abstract class HomogenizationTaskBase : PlcTaskBase
{
    protected HomogenizationTaskBase(
        IPlcBuffer buffer,
        ILogicalSignalAccessor<HomogenizationSignal> signals,
        HomogenizationContext context,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationCodeOptions> codeOptions,
        IOptions<HomogenizationModuleOptions> moduleOptions)
        : base(buffer, context, logger)
    {
        ModuleContext = context;
        ProductionTime = productionTime;
        CodeOptions = codeOptions.Value;
        var runtime = moduleOptions.Value.Runtime;
        EventLoopInterval = Math.Max(runtime.MinEventLoopIntervalMs, runtime.EventLoopIntervalMs);
        Signals = signals;
        Codec = new HomogenizationSignalCodec(signals, productionTime);
    }

    /// <summary>
    /// 匀浆运行态上下文，用于记录最近一次业务结果并供 UI 展示。
    /// </summary>
    protected HomogenizationContext ModuleContext { get; }

    /// <summary>
    /// 匀浆任务使用的统一生产业务时间服务，避免对外状态和 payload 混用 UTC/本地时间。
    /// </summary>
    protected IProductionTimeProvider ProductionTime { get; }

    /// <summary>
    /// 匀浆 PLC/MES code 配置，包含触发码、复位码、应答码和 MES 通道名称。
    /// </summary>
    protected HomogenizationCodeOptions CodeOptions { get; }

    /// <summary>
    /// 触发-应答任务循环间隔，已按配置的最小值保护。
    /// </summary>
    protected int EventLoopInterval { get; }

    /// <summary>
    /// Runtime 提供的强类型 PLC 信号访问器，任务只能通过枚举键读写点位。
    /// </summary>
    protected ILogicalSignalAccessor<HomogenizationSignal> Signals { get; }

    /// <summary>
    /// 匀浆业务快照解码器，只负责把 PLC 连续区转换为插件 payload。
    /// </summary>
    protected HomogenizationSignalCodec Codec { get; }

    /// <summary>
    /// PLC 任务循环间隔，来源于匀浆运行配置。
    /// </summary>
    protected override int TaskLoopInterval => EventLoopInterval;

    protected ushort ResolveAck(MesCallResult result)
        => result.IsSuccess
            ? CodeOptions.Plc.AckOk
            : result.Outcome == MesCallOutcome.BusinessRejected
                ? CodeOptions.Plc.AckMesNg
                : CodeOptions.Plc.AckException;
}
