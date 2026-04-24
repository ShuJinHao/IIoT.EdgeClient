using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Runtime.Base;

namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

internal sealed class HomogenizationHeartbeatTask : HeartbeatMirrorPlcTaskBase
{
    private readonly HomogenizationContext _context;
    private readonly int _taskLoopInterval;
    private HomogenizationSignalCodec? _codec;

    public HomogenizationHeartbeatTask(
        IPlcBuffer buffer,
        HomogenizationContext context,
        ILogService logger,
        HomogenizationModuleOptions moduleOptions,
        HomogenizationCodeOptions codeOptions)
        : base(buffer, context, logger)
    {
        _context = context;
        _taskLoopInterval = Math.Max(20, moduleOptions.Runtime.EventLoopIntervalMs);
    }

    public override string TaskName => "Homogenization.Heartbeat";

    protected override int TaskLoopInterval => _taskLoopInterval;

    protected override string InputLabel => HomogenizationPlcSignalProfile.HeartbeatIn.Label;

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
        _context.LastHeartbeatAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    private HomogenizationSignalCodec Codec => _codec ??= new HomogenizationSignalCodec(Buffer, _context);
}
