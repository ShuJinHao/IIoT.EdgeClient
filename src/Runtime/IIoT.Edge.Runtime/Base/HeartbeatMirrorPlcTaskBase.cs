using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.Base;

public abstract class HeartbeatMirrorPlcTaskBase : PlcTaskBase
{
    protected HeartbeatMirrorPlcTaskBase(IPlcBuffer buffer, ProductionContext context, ILogService logger)
        : base(buffer, context, logger)
    {
    }

    protected abstract string InputLabel { get; }

    protected abstract string OutputLabel { get; }

    protected abstract ushort ReadWord(string label);

    protected abstract void WriteWord(string label, ushort value);

    protected virtual ushort NormalizeHeartbeat(ushort value)
        => value == 0 ? (ushort)1 : value;

    protected virtual Task OnHeartbeatMirroredAsync(ushort input, ushort output, CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected override async Task DoCoreAsync()
    {
        var input = ReadWord(InputLabel);
        var output = NormalizeHeartbeat(input);

        WriteWord(OutputLabel, output);
        Context.Set($"Runtime.Tasks.{TaskName}.LastHeartbeatAtUtc", DateTime.UtcNow);
        Context.Set($"Runtime.Tasks.{TaskName}.LastHeartbeatIn", input);
        Context.Set($"Runtime.Tasks.{TaskName}.LastHeartbeatOut", output);

        await OnHeartbeatMirroredAsync(input, output, TaskCancellationToken).ConfigureAwait(false);
    }
}
