using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.Sdk.Base;

/// <summary>
/// 强类型心跳镜像任务基类，插件只提供输入/输出信号枚举，不直接写 PLC 字符串 SignalKey。
/// </summary>
/// <typeparam name="TSignalKey">插件声明的 PLC 信号枚举。</typeparam>
public abstract class HeartbeatMirrorPlcTaskBase<TSignalKey> : PlcTaskBase
    where TSignalKey : struct, Enum
{
    private readonly ILogicalSignalAccessor<TSignalKey> _signals;

    protected HeartbeatMirrorPlcTaskBase(
        IPlcBuffer buffer,
        ILogicalSignalAccessor<TSignalKey> signals,
        ProductionContext context,
        ILogService logger)
        : base(buffer, context, logger)
    {
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
    }

    protected abstract TSignalKey InputSignal { get; }

    protected abstract TSignalKey OutputSignal { get; }

    protected virtual ushort NormalizeHeartbeat(ushort value)
        => value == 0 ? (ushort)1 : value;

    protected virtual Task OnHeartbeatMirroredAsync(ushort input, ushort output, CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected override async Task DoCoreAsync()
    {
        var input = _signals.ReadUInt16(InputSignal);
        var output = NormalizeHeartbeat(input);

        _signals.WriteUInt16(OutputSignal, output);
        Context.Set($"Runtime.Tasks.{TaskName}.LastHeartbeatAtUtc", DateTime.UtcNow);
        Context.Set($"Runtime.Tasks.{TaskName}.LastHeartbeatIn", input);
        Context.Set($"Runtime.Tasks.{TaskName}.LastHeartbeatOut", output);

        await OnHeartbeatMirroredAsync(input, output, TaskCancellationToken).ConfigureAwait(false);
    }
}
