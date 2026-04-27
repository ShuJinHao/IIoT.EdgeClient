using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Runtime.Base;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

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

    protected HomogenizationContext ModuleContext { get; }

    protected HomogenizationCodeOptions CodeOptions { get; }

    protected int EventLoopInterval { get; }

    protected HomogenizationSignalCodec Codec => _codec ??= new HomogenizationSignalCodec(Buffer, ModuleContext);

    protected override int TaskLoopInterval => EventLoopInterval;

    protected ushort ResolveAck(MesCallResult result)
        => result.IsSuccess
            ? CodeOptions.Plc.AckOk
            : result.Outcome == MesCallOutcome.BusinessRejected
                ? CodeOptions.Plc.AckMesNg
                : CodeOptions.Plc.AckException;
}
