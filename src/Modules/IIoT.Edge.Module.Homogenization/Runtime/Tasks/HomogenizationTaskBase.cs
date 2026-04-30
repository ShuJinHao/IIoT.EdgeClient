using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Runtime.Base;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

/// <summary>
/// 匀浆握手类任务基类，统一提供 PLC 信号码表、循环间隔和 MES 结果到 PLC 应答码的转换。
/// </summary>
internal abstract class HomogenizationTaskBase : PlcTaskBase
{
    private HomogenizationSignalCodec? _codec;

    protected HomogenizationTaskBase(
        IPlcBuffer buffer,
        HomogenizationContext context,
        ILogService logger,
        IOptions<HomogenizationCodeOptions> codeOptions,
        IOptions<HomogenizationModuleOptions> moduleOptions)
        : base(buffer, context, logger)
    {
        ModuleContext = context;
        CodeOptions = codeOptions.Value;
        var runtime = moduleOptions.Value.Runtime;
        EventLoopInterval = Math.Max(runtime.MinEventLoopIntervalMs, runtime.EventLoopIntervalMs);
    }

    /// <summary>
    /// 匀浆运行态上下文，用于记录最近一次业务结果并供 UI 展示。
    /// </summary>
    protected HomogenizationContext ModuleContext { get; }

    /// <summary>
    /// 匀浆 PLC/MES code 配置，包含触发码、复位码、应答码和 MES 通道名称。
    /// </summary>
    protected HomogenizationCodeOptions CodeOptions { get; }

    /// <summary>
    /// 触发-应答任务循环间隔，已按配置的最小值保护。
    /// </summary>
    protected int EventLoopInterval { get; }

    /// <summary>
    /// 匀浆 PLC 信号编解码器，按本插件信号模板读写地址。
    /// </summary>
    protected HomogenizationSignalCodec Codec => _codec ??= new HomogenizationSignalCodec(Buffer, ModuleContext);

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
